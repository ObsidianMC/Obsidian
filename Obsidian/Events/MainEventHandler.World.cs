using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Events;

public partial class MainEventHandler
{
    [EventPriority(Priority = Priority.Internal)]
    public async ValueTask OnBlockBreak(BlockBreakEventArgs args)
    {
        var player = args.Player;
        var sequence = args.Sequence;
        var block = args.Block;
        var world = player.World;
        var location = args.Location;

        player.Client.SendPacket(new BlockChangedAckPacket
        {
            SequenceID = sequence
        });

        if (args.IsCancelled)
        {
            player.Client.SendPacket(new BlockUpdatePacket(location, block.GetHashCode()));
            return;
        }

        await world.SetBlockAsync(location, BlocksRegistry.Air, true);

        player.Client.SendPacket(new BlockUpdatePacket(location, BlocksRegistry.Air.GetHashCode()));

        world.PacketBroadcaster.QueuePacketToWorld(world, 0, new BlockDestructionPacket
        {
            EntityId = player.EntityId,
            Position = location,
            DestroyStage = -1
        }, player.EntityId);

        var droppedItem = ItemsRegistry.GetSingleItem(block.Material);

        if (droppedItem.Type == Material.Air)
            return;

        var item = new ItemEntity
        {
            EntityId = Server.GetNextEntityId(),
            Item = droppedItem,
            World = player.World,
            Position = (VectorF)location + 0.5f,
        };

        player.World.TryAddEntity(item);

        var power = GetRandDropVelocity();
        var direction = Globals.Random.NextFloat() * 6.2f;

        item.SpawnEntity(new Velocity(-Math.Sin(direction) * power, 0.2f, Math.Cos(direction) * power));
    }

    private static float GetRandDropVelocity()
    {
        var f = Globals.Random.NextFloat();

        return f * 0.5f;
    }
}
