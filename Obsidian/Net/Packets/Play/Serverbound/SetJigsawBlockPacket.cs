using Obsidian.Entities;
using Obsidian.WorldData;
using Obsidian.WorldData.Structures;

namespace Obsidian.Net.Packets.Play.Serverbound;

/// <summary>Decodes and applies edits from the jigsaw block editor.</summary>
public partial class SetJigsawBlockPacket
{
    public Vector Position { get; private set; }

    internal JigsawBlockSettings Settings { get; private set; } = new();

    public override void Populate(INetStreamReader reader)
    {
        this.Position = reader.ReadPosition();
        var name = ReadIdentifier(reader);
        var target = ReadIdentifier(reader);
        var pool = ReadIdentifier(reader);
        var finalState = reader.ReadString();
        var joint = reader.ReadString() == "rollable" ? JigsawJointType.Rollable : JigsawJointType.Aligned;
        this.Settings = new JigsawBlockSettings
        {
            Name = name,
            Target = target,
            Pool = pool,
            FinalState = finalState,
            Joint = joint,
            SelectionPriority = reader.ReadVarInt(),
            PlacementPriority = reader.ReadVarInt()
        };
    }

    private static string ReadIdentifier(INetStreamReader reader) =>
        JigsawBlocks.TryIdentifier(reader.ReadString(), out var id)
            ? id : throw new FormatException("Invalid jigsaw identifier");

    public override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        if (!CommandBlockMinecart.CanEdit(player) || player.Level is not AbstractLevel level)
            return ValueTask.CompletedTask;

        lock (level.Fluids.TickLock)
        {
            if (level.GetResidentBlockEntity(this.Position) is JigsawBlockEntity entity)
            {
                entity.Settings = this.Settings with { Components = entity.Settings.Components };
                entity.SetChanged();
                entity.SendUpdate();
            }
        }

        return ValueTask.CompletedTask;
    }
}
