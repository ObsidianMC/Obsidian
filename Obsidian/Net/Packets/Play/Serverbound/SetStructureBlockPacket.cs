using Obsidian.Entities;
using Obsidian.WorldData;
using Obsidian.WorldData.Structures;

namespace Obsidian.Net.Packets.Play.Serverbound;

/// <summary>Edits from the structure block editor, including its SAVE, LOAD and detect-size buttons.</summary>
public partial class SetStructureBlockPacket
{
    public Vector Position { get; private set; }

    internal int Action { get; private set; }

    private string suppliedName = string.Empty;

    internal StructureBlockSettings Settings { get; private set; } = new();

    public override void Populate(INetStreamReader reader)
    {
        this.Position = reader.ReadPosition();
        this.Action = reader.ReadVarInt();
        var mode = reader.ReadVarInt();
        this.suppliedName = reader.ReadString();
        var offset = new Vector(ReadAxis(reader, -48, 48), ReadAxis(reader, -48, 48), ReadAxis(reader, -48, 48));
        var size = new Vector(ReadAxis(reader, 0, 48), ReadAxis(reader, 0, 48), ReadAxis(reader, 0, 48));
        var mirror = reader.ReadVarInt();
        var rotation = reader.ReadVarInt();
        var metadata = reader.ReadString(128);
        var integrity = Math.Clamp(reader.ReadSingle(), 0, 1);
        var seed = reader.ReadVarLong();
        var flags = reader.ReadByte();
        if (this.Action is < 0 or > 3 || mode is < 0 or > 3 || mirror is < 0 or > 2 || rotation is < 0 or > 3)
            throw new FormatException("Invalid structure block enum");

        this.Settings = new StructureBlockSettings
        {
            Name = StructureBlocks.NormalizeName(this.suppliedName),
            Mode = (StructureBlockMode)mode,
            Offset = offset,
            Size = size,
            Mirror = (StructureMirror)mirror,
            Rotation = (StructureRotation)rotation,
            Metadata = metadata,
            IgnoreEntities = (flags & 1) != 0,
            ShowAir = (flags & 2) != 0,
            ShowBoundingBox = (flags & 4) != 0,
            Strict = (flags & 8) != 0,
            Integrity = integrity,
            Seed = seed
        };
    }

    private static int ReadAxis(INetStreamReader reader, int min, int max) =>
        Math.Clamp((int)unchecked((sbyte)reader.ReadByte()), min, max);

    public override async ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        // Vanilla silently ignores unauthorized edits, unlike command blocks' rejection message.
        if (!CommandBlockMinecart.CanEdit(player) || player.Level is not AbstractLevel level)
            return;

        var message = await StructureBlocks.EditAsync(level, this.Position, this.Settings, this.Action,
            this.suppliedName);
        if (message is not null)
            await player.SendMessageAsync(ChatMessage.Simple(message));
    }
}
