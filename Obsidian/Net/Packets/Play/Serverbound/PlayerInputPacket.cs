using Obsidian.Entities;
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

    public override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        var wasSneaking = player.Sneaking;
        var previous = player.Input;

        player.Input = this.Flags;
        (player as PlayerBase)?.TrackJumpInput(previous, this.Flags);

        if (player.Sneaking != wasSneaking)
        {
            player.Level.PacketBroadcaster.QueuePacketToLevel(player.Level, new SetEntityDataPacket
            {
                EntityId = player.EntityId,
                Entity = player
            }, player.EntityId);
        }    

        return default;
    }
}

