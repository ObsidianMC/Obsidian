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
        var world = args.World;
        var location = args.Location;

        if (args.IsCancelled)
            return;

        await world.SetBlockAsync(location, BlocksRegistry.Air, true);

        await player.Client.QueuePacketAsync(new BlockChangedAckPacket
        {
            SequenceID = sequence
        });

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

        item.SpawnEntity(Velocity.Zero);
    }

    private static float GetRandDropVelocity()
    {
        var f = Globals.Random.NextFloat();

        return f * 0.5f;
    }
}
