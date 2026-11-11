using Obsidian.API.BlockEntities;
using Obsidian.Nbt;
using Obsidian.WorldData.Structures;

namespace Obsidian.WorldData;

/// <summary>The editor's mode, in the protocol's order.</summary>
internal enum StructureBlockMode
{
    Save,
    Load,
    Corner,
    Data
}

/// <summary>
/// A structure editor snapshot. Replacing the whole record lets chunk saves read coherent fields without taking
/// the tick lock beneath a chunk lock. Edits and redstone replace it under the tick lock.
/// </summary>
internal sealed record StructureBlockSettings
{
    internal long Revision { get; init; }

    public string Name { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Metadata { get; init; } = string.Empty;
    public Vector Offset { get; init; } = new(0, 1, 0);
    public Vector Size { get; init; }
    public StructureRotation Rotation { get; init; }
    public StructureMirror Mirror { get; init; }
    public StructureBlockMode Mode { get; init; } = StructureBlockMode.Load;
    public bool IgnoreEntities { get; init; } = true;
    public bool Strict { get; init; }
    public bool Powered { get; init; }
    public bool ShowAir { get; init; }
    public bool ShowBoundingBox { get; init; } = true;
    public float Integrity { get; init; } = 1;
    public long Seed { get; init; }
}

/// <summary>A structure block's saved settings and the data its client-side editor reads.</summary>
internal sealed class StructureBlockEntity : BlockEntity
{
    public const string TypeId = "minecraft:structure_block";

    // Vanilla's legacy save names differ from the lower-case block state properties and protocol ordinals.
    internal static readonly string[] ModeNames = ["SAVE", "LOAD", "CORNER", "DATA"];
    internal static readonly string[] RotationNames = ["NONE", "CLOCKWISE_90", "CLOCKWISE_180", "COUNTERCLOCKWISE_90"];
    internal static readonly string[] MirrorNames = ["NONE", "LEFT_RIGHT", "FRONT_BACK"];

    private volatile StructureBlockSettings settings = new();

    internal StructureBlockSettings Settings
    {
        get => this.settings;
        set => this.settings = value;
    }

    internal override bool SavesOnAnyThread => true;

    protected internal override NbtCompound GetUpdateData() => BlockEntityNbt.SaveData(this);

    protected internal override void Save(NbtCompound tag)
    {
        var data = this.Settings;
        tag.Add(new NbtTag<string>("name", data.Name));
        tag.Add(new NbtTag<string>("author", data.Author));
        tag.Add(new NbtTag<string>("metadata", data.Metadata));

        tag.Add(new NbtTag<int>("posX", data.Offset.X));
        tag.Add(new NbtTag<int>("posY", data.Offset.Y));
        tag.Add(new NbtTag<int>("posZ", data.Offset.Z));
        tag.Add(new NbtTag<int>("sizeX", data.Size.X));
        tag.Add(new NbtTag<int>("sizeY", data.Size.Y));
        tag.Add(new NbtTag<int>("sizeZ", data.Size.Z));

        tag.Add(new NbtTag<string>("rotation", RotationNames[(int)data.Rotation]));
        tag.Add(new NbtTag<string>("mirror", MirrorNames[(int)data.Mirror]));
        tag.Add(new NbtTag<string>("mode", ModeNames[(int)data.Mode]));

        tag.Add(new NbtTag<bool>("ignoreEntities", data.IgnoreEntities));
        tag.Add(new NbtTag<bool>("strict", data.Strict));
        tag.Add(new NbtTag<bool>("powered", data.Powered));
        tag.Add(new NbtTag<bool>("showair", data.ShowAir));
        tag.Add(new NbtTag<bool>("showboundingbox", data.ShowBoundingBox));

        tag.Add(new NbtTag<float>("integrity", data.Integrity));
        tag.Add(new NbtTag<long>("seed", data.Seed));
    }

    protected internal override void Load(NbtCompound tag)
    {
        this.Settings = new StructureBlockSettings
        {
            Revision = this.Settings.Revision + 1,
            Name = StructureBlocks.NormalizeName(Text(tag, "name")),
            Author = Text(tag, "author"),
            Metadata = Text(tag, "metadata"),
            Offset = new Vector(
                Integer(tag, "posX", 0, -48, 48),
                Integer(tag, "posY", 1, -48, 48),
                Integer(tag, "posZ", 0, -48, 48)),
            Size = new Vector(
                Integer(tag, "sizeX", 0, 0, 48),
                Integer(tag, "sizeY", 0, 0, 48),
                Integer(tag, "sizeZ", 0, 0, 48)),
            Rotation = (StructureRotation)Ordinal(tag, "rotation", RotationNames, 0),
            Mirror = (StructureMirror)Ordinal(tag, "mirror", MirrorNames, 0),
            Mode = (StructureBlockMode)Ordinal(tag, "mode", ModeNames, 3),
            IgnoreEntities = Flag(tag, "ignoreEntities", true),
            Strict = Flag(tag, "strict", false),
            Powered = Flag(tag, "powered", false),
            ShowAir = Flag(tag, "showair", false),
            ShowBoundingBox = Flag(tag, "showboundingbox", true),
            Integrity = tag.TryGetNumber<float>("integrity", out var integrity) ? integrity : 1,
            Seed = tag.TryGetNumber<long>("seed", out var seed) ? seed : 0
        };

        this.UpdateMode();
    }

    protected internal override void OnLoad() => this.UpdateMode();

    private void UpdateMode()
    {
        if (this.Level is not AbstractLevel level)
            return;

        lock (level.Fluids.TickLock)
            StructureBlocks.UpdateMode(level, this);
    }

    private static string Text(NbtCompound tag, string name) =>
        tag.TryGetTagValue<string>(name, out var text) ? text : string.Empty;

    private static bool Flag(NbtCompound tag, string name, bool fallback) =>
        tag.TryGetBool(name, out var flag) ? flag : fallback;

    private static int Integer(NbtCompound tag, string name, int fallback, int min, int max) =>
        Math.Clamp(tag.TryGetNumber<int>(name, out var value) ? value : fallback, min, max);

    private static int Ordinal(NbtCompound tag, string name, string[] names, int fallback)
    {
        var index = Array.IndexOf(names, Text(tag, name));
        return index >= 0 ? index : fallback;
    }
}
