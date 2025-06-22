using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;
public partial class PlayerInputPacket
{
    [Field(0), ActualType(typeof(byte))]
    public PlayerInput Flags { get; set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Flags = (PlayerInput)reader.ReadByte();
    }

    public override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        player.Input = this.Flags;
        player.Sneaking = this.Flags.HasFlag(PlayerInput.Sneak);

        player.World.PacketBroadcaster.QueuePacketToWorld(player.World, new SetEntityDataPacket
        {
            EntityId = player.EntityId,
            Entity = player
        }, player.EntityId);

        return default;
    }
}

