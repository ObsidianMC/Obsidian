using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class PlayerCommandPacket
{
    [Field(0), VarLength]
    public int EntityId { get; private set; }

    [Field(1), ActualType(typeof(int)), VarLength]
    public EAction Action { get; set; }

    [Field(2), VarLength]
    public int JumpBoost { get; set; }

    public override void Populate(INetStreamReader reader)
    {
        this.EntityId = reader.ReadVarInt();
        this.Action = reader.ReadVarInt<EAction>();
        this.JumpBoost = reader.ReadVarInt();
    }

    public async override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        var block = await player.World.GetBlockAsync((int)player.Position.X, (int)player.HeadY, (int)player.Position.Z);

        switch (Action)
        {
            case EAction.StartSneaking:
                player.Sneaking = true;
                break;
            case EAction.StopSneaking:
                player.Sneaking = false;
                player.Pose = Pose.Standing;
                break;
            case EAction.LeaveBed:
                player.Sleeping = false;
                break;
            case EAction.StartSprinting:
                if ((bool)(block?.IsLiquid))
                    player.Swimming = true;

                player.Sprinting = true;
                break;
            case EAction.StopSprinting:
                if (player.Swimming)
                    player.Swimming = false;

                player.Sprinting = false;
                break;
            case EAction.StartJumpWithHorse:
                break;
            case EAction.StopJumpWithHorse:
                break;
            case EAction.OpenHorseInventory:
                player.InHorseInventory = true;
                break;
            case EAction.StartFlyingWithElytra:
                player.FlyingWithElytra = true;
                break;
        }

        player.World.PacketBroadcaster.QueuePacketToWorld(player.World, new SetEntityDataPacket
        {
            EntityId = player.EntityId,
            Entity = player
        }, player.EntityId);
    }
}

public enum EAction : int
{
    StartSneaking,
    StopSneaking,

    LeaveBed,

    StartSprinting,
    StopSprinting,

    StartJumpWithHorse,
    StopJumpWithHorse,
    OpenHorseInventory,

    StartFlyingWithElytra
}
