using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Utilities;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Block changes go out once per tick, per chunk section, like vanilla's chunk broadcast; prediction acknowledgements
/// go out behind them. <see cref="VanillaBlockChangePackets"/> replays vanilla's order; these cover what it can't.
/// </summary>
public sealed class SectionBlocksUpdates
{
    private static readonly Vector Target = new(8, 64, 8);

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ChangesOutsideTheTickGoOutAtTheNextFlushGroupedBySection()
    {
        await using var context = TestServerFactory.CreateServer($"section-updates-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        WorldBlockEntities.AddEmptyChunk(world, 1, 0);
        await world.GetBlockAsync(Target);
        var (player, client) = Join(context, new VectorD(8.5, 80, 8.5));
        var stranger = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Stranger");
        Assert.True(world.TryAddPlayer(stranger));
        stranger.LoadedChunks.Add(NumericsHelper.IntsToLong(1, 0));

        // A plugin's writes, outside any tick: two in the section above, and one in the section below, stored twice.
        await world.SetBlockAsync(new Vector(1, 81, 2), BlocksRegistry.Stone);
        await world.SetBlockAsync(new Vector(3, 64, 4), BlocksRegistry.Dirt);
        await world.SetBlockAsync(new Vector(5, 82, 6), BlocksRegistry.Stone);
        await world.SetBlockAsync(new Vector(3, 64, 4), BlocksRegistry.Cobblestone);
        Assert.Empty(BlockUpdates.SentBlocks(client));

        world.FlushBlockChanges();

        var packets = client.SentPackets.Where(packet => packet is BlockUpdatePacket or SectionBlocksUpdatePacket)
            .ToArray();
        Assert.Equal(2, packets.Length);
        var below = Assert.IsType<BlockUpdatePacket>(packets[0]);
        Assert.Equal((new Vector(3, 64, 4), BlocksRegistry.Cobblestone.GetHashCode()), (below.Position, below.BlockId));
        var above = Assert.IsType<SectionBlocksUpdatePacket>(packets[1]);
        Assert.Equal((0, 5, 0), (above.SectionX, above.SectionY, above.SectionZ));
        var stone = BlocksRegistry.Stone.GetHashCode();
        Assert.Equal(
            [(new Vector(1, 81, 2), stone), (new Vector(5, 82, 6), stone)],
            above.Blocks().OrderBy(block => block.Position.X).ToArray());
        Assert.Empty(BlockUpdates.SentBlocks((TestClient)stranger.Client));

        // Each change goes out once.
        client.ClearPackets();
        world.FlushBlockChanges();
        Assert.Empty(BlockUpdates.SentBlocks(client));
        Assert.Same(player, client.Player);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AcknowledgementWaitsForTheWorldTickThatSendsItsChanges()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gated = false;
        World? ticked = null;
        await using var context = TestServerFactory.CreateServer($"section-acks-{Guid.NewGuid():N}",
            world => new FakeWorldManager(world)
            {
                TickWorlds = async () =>
                {
                    if (gated)
                    {
                        entered.SetResult();
                        await release.Task;
                    }

                    await ticked!.DoWorldTickAsync();
                },
            });
        var world = ticked = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(Target);
        world.BlockUpdates.SetBlock(Target, BlocksRegistry.Stone);
        var (player, client) = Join(context, new VectorD(8.5, 64, 10.5));
        var placed = Target + Vector.Up;

        // A use between ticks: its change and acknowledgement wait for the next world tick, change first.
        await BlockUpdates.UseOn(Target, BlockFace.Up, sequence: 3).HandleAsync(context.Server, player);
        client.ClearPackets();
        await context.Server.TickConnectionsAsync(sendKeepAlive: false);
        Assert.DoesNotContain(client.SentPackets, packet => packet is BlockChangedAckPacket);

        await context.Server.TickWorldsOnceAsync();
        AssertChangeThenAcknowledgement(client, placed, BlocksRegistry.Stone, 3);

        // A use received before a world tick began but finishing during it may miss the tick's flush: its
        // acknowledgement waits for the tick after. The held inventory keeps the use waiting until the tick has begun.
        Task use;
        using (await player.Inventory.LockAsync())
        {
            use = BlockUpdates.UseOn(placed, BlockFace.Up, sequence: 4).HandleAsync(context.Server, player).AsTask();
            Assert.False(use.IsCompleted);
            gated = true;
            var tick = context.Server.TickWorldsOnceAsync();
            await entered.Task;
            gated = false;
            release.SetResult();
            await tick;
        }

        await use;
        await context.Server.TickConnectionsAsync(sendKeepAlive: false);
        Assert.DoesNotContain(client.SentPackets, packet => packet is BlockChangedAckPacket);

        await context.Server.TickWorldsOnceAsync();
        AssertChangeThenAcknowledgement(client, placed + Vector.Up, BlocksRegistry.Stone, 4);
    }

    private static (Entities.Player Player, TestClient Client) Join(RealServerContext context, VectorD position)
    {
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Viewer", position);
        player.GameMode = GameMode.Creative;
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Stone, 1));
        Assert.True(context.World.TryAddPlayer(player));
        player.LoadedChunks.Add(NumericsHelper.IntsToLong(0, 0));

        var client = (TestClient)player.Client;
        client.ClearPackets();
        return (player, client);
    }

    private static void AssertChangeThenAcknowledgement(TestClient client, Vector position, IBlock block, int sequence)
    {
        var packets = client.SentPackets.ToList();
        var change = packets.FindIndex(packet => packet switch
        {
            BlockUpdatePacket update => update.Position == position && update.BlockId == block.GetHashCode(),
            SectionBlocksUpdatePacket section => section.Blocks().Contains((position, block.GetHashCode())),
            _ => false,
        });
        var acknowledgement = packets.FindIndex(packet => packet is BlockChangedAckPacket ack
            && ack.SequenceID == sequence);
        Assert.True(change >= 0, "The client wasn't sent the change.");
        Assert.True(acknowledgement > change, "The acknowledgement didn't follow the change.");
        client.ClearPackets();
    }
}
