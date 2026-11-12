using Obsidian.API.BlockEntities;
using Obsidian.Nbt;
using Obsidian.WorldData.Structures;

namespace Obsidian.WorldData;

/// <summary>An atomic snapshot of the jigsaw editor's saved fields.</summary>
internal sealed record JigsawBlockSettings
{
    public string Name { get; init; } = "minecraft:empty";
    public string Target { get; init; } = "minecraft:empty";
    public string Pool { get; init; } = "minecraft:empty";
    public string FinalState { get; init; } = "minecraft:air";
    public JigsawJointType? Joint { get; init; } = JigsawJointType.Rollable;
    public int SelectionPriority { get; init; }
    public int PlacementPriority { get; init; }
    public NbtCompound? Components { get; init; }
}

/// <summary>The jigsaw editor's persistent settings and client update data.</summary>
internal sealed class JigsawBlockEntity : BlockEntity
{
    internal const string TypeId = "minecraft:jigsaw";

    private volatile JigsawBlockSettings settings = new();

    internal JigsawBlockSettings Settings
    {
        get => this.settings;
        set => this.settings = value;
    }

    internal override bool SavesOnAnyThread => true;

    protected internal override NbtCompound GetUpdateData()
    {
        // Vanilla's editor update is saveCustomOnly: stored components are deliberately absent on the wire.
        var tag = BlockEntityNbt.SaveData(this);
        tag.Remove("components");
        return tag;
    }

    protected internal override void Save(NbtCompound tag)
    {
        var data = this.Settings;
        var joint = data.Joint ?? this.DefaultJoint();
        tag.Add(new NbtTag<string>("name", data.Name));
        tag.Add(new NbtTag<string>("target", data.Target));
        tag.Add(new NbtTag<string>("pool", data.Pool));
        tag.Add(new NbtTag<string>("final_state", data.FinalState));
        tag.Add(new NbtTag<string>("joint", joint == JigsawJointType.Rollable ? "rollable" : "aligned"));
        tag.Add(new NbtTag<int>("selection_priority", data.SelectionPriority));
        tag.Add(new NbtTag<int>("placement_priority", data.PlacementPriority));
        if (data.Components is { Count: > 0 } components)
            tag.Add("components", DataBlockEntity.Copy(components));
    }

    protected internal override void Load(NbtCompound tag)
    {
        this.Settings = new JigsawBlockSettings
        {
            Name = ReadIdentifier(tag, "name"),
            Target = ReadIdentifier(tag, "target"),
            Pool = ReadIdentifier(tag, "pool"),
            FinalState = tag.TryGetTagValue<string>("final_state", out var finalState) ? finalState : "minecraft:air",
            Joint = tag.TryGetTagValue<string>("joint", out var joint) ? joint switch
            {
                "rollable" => JigsawJointType.Rollable,
                "aligned" => JigsawJointType.Aligned,
                _ => null
            } : null,
            SelectionPriority = tag.TryGetNumber<int>("selection_priority", out var selection) ? selection : 0,
            PlacementPriority = tag.TryGetNumber<int>("placement_priority", out var placement) ? placement : 0,
            Components = tag.TryGetTag<NbtCompound>("components", out var components)
                ? DataBlockEntity.Copy(components) : null
        };

        if (this.Settings.Joint is null && this.Level is not null)
            this.Settings = this.Settings with { Joint = this.DefaultJoint() };
    }

    protected internal override void OnLoad()
    {
        if (this.Settings.Joint is null)
            this.Settings = this.Settings with { Joint = this.DefaultJoint() };
    }

    /// <summary>Resolves legacy data while its chunk has the state, before a level or first tick exists.</summary>
    internal void ResolveJointDefault(IBlock state)
    {
        if (this.Settings.Joint is null)
        {
            var vertical = state.Is(BlockIds.Jigsaw)
                && JigsawBlockInfo.GetFrontFacing(state) is BlockFace.Up or BlockFace.Down;
            this.Settings = this.Settings with
            {
                Joint = vertical ? JigsawJointType.Rollable : JigsawJointType.Aligned
            };
        }
    }

    // Chunk loading constructs entities before attaching their level. Resolve legacy joint defaults only once
    // their actual block state is available; a newly constructed entity always starts rollable in vanilla.
    private JigsawJointType DefaultJoint()
    {
        if (this.Level is AbstractLevel level)
        {
            // Saves may already hold a chunk EntityLock. Read its grid directly without taking the tick lock
            // or accessing the tick-owned block cache underneath it.
            var position = this.BlockPosition;
            var state = level.GetLoadedChunk(position.X >> 4, position.Z >> 4)
                ?.GetBlock(position.X, position.Y, position.Z);
            if (state is not null && state.Is(BlockIds.Jigsaw)
                && JigsawBlockInfo.GetFrontFacing(state) is BlockFace.Up or BlockFace.Down)
            {
                return JigsawJointType.Rollable;
            }
        }

        return JigsawJointType.Aligned;
    }

    private static string ReadIdentifier(NbtCompound tag, string name) =>
        tag.TryGetTagValue<string>(name, out var text) && JigsawBlocks.TryIdentifier(text, out var id)
            ? id : "minecraft:empty";
}
