using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class PlayerActionPacket : IServerboundPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public DiggingStatus Status { get; private set; }

    [Field(1)]
    public Vector Position { get; private set; }

    [Field(2), ActualType(typeof(sbyte))]
    public BlockFace Face { get; private set; } // This is an enum of what face of the block is being hit

    [Field(3), VarLength]
    public int Sequence { get; private set; }

    public int Id => 0x1C;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        IBlock? b = await player.world.GetBlockAsync(Position);
        if (b is not IBlock block)
            return;

        if (Status == DiggingStatus.FinishedDigging || (Status == DiggingStatus.StartedDigging && player.Gamemode == Gamemode.Creative))
        {
            await player.world.SetBlockUntrackedAsync(Position, BlocksRegistry.Air, true);

            var blockBreakEvent = await server.Events.InvokeBlockBreakAsync(new BlockBreakEventArgs(server, player, block, Position));
            if (blockBreakEvent.IsCancelled)
                return;
        }

        await server.BroadcastPlayerDigAsync(new PlayerDiggingStore
        {
            Player = player.Uuid,
            Packet = this
        }, block);

        if (Status == DiggingStatus.FinishedDigging)
        {
            await player.world.BlockUpdateNeighborsAsync(new BlockUpdate(player.world, Position));
        }
    }
}

public class PlayerDiggingStore
{
    public Guid Player { get; init; }
    public PlayerActionPacket Packet { get; init; }
}

public enum DiggingStatus : int
{
    StartedDigging,
    CancelledDigging,
    FinishedDigging,

    DropItemStack,
    DropItem,

    ShootArrowOrFinishEating,

    SwapItemInHand
}
