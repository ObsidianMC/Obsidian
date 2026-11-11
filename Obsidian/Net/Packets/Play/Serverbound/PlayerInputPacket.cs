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
        var previousPose = player.Pose;

        player.Input = this.Flags;
        player.TrackJumpInput(previous, this.Flags);

        // Sneaking crouches at once; a pose that changes is sent with the change.
        if (player is PlayerBase posed)
            posed.UpdatePose();

        if (player.Sneaking != wasSneaking && player.Pose == previousPose)
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

