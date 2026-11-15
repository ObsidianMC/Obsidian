using Obsidian.Entities;

namespace Obsidian.Net.Packets.Play.Serverbound;

/// <summary>A player's middle click on an entity.</summary>
public partial class PickItemFromEntityPacket
{
    public int EntityId { get; private set; }

    public bool IncludeData { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.EntityId = reader.ReadVarInt();
        this.IncludeData = reader.ReadBoolean();
    }

    public override ValueTask HandleAsync(IServer server, IClientPlayer player) =>
        player is PlayerBase picker
            ? picker.PickEntityAsync(this.EntityId)
            : ValueTask.CompletedTask;
}
