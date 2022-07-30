using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class PlayerDigging : IServerboundPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public DiggingStatus Status { get; private set; }

    [Field(1)]
    public Vector Position { get; private set; }

    [Field(2), ActualType(typeof(sbyte))]
    public BlockFace Face { get; private set; } // This is an enum of what face of the block is being hit

    public int Id => 0x1A;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        Block? b = await player.World.GetBlockAsync(Position);
        if (b is not Block block)
            return;

        if (Status == DiggingStatus.FinishedDigging || (Status == DiggingStatus.StartedDigging && player.Gamemode == Gamemode.Creative))
        {
            await player.World.SetBlockUntrackedAsync(Position, Block.Air, true);

            var blockBreakEvent =
                await server.Events.InvokeBlockBreakAsync(
                    new BlockBreakEventArgs(player.World, player, block, Position));

            if (blockBreakEvent.Cancelled)
                return;
        }

        await server.BroadcastPlayerDigAsync(new PlayerDiggingStore
        {
            Player = player.Uuid,
            Packet = this
        }, block);

        if (Status == DiggingStatus.FinishedDigging)
        {
            await player.World.BlockUpdateNeighborsAsync(new BlockUpdate(player.World, Position));
        }
    }
}

public class PlayerDiggingStore
{
    public Guid Player { get; init; }
    public PlayerDigging Packet { get; init; }
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
