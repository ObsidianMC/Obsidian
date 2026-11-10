#nullable enable

using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.WorldData.Redstone;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// A dispenser spends the item it fires and fires it in one step: an item is never both fired and taken by a player's
/// click or a hopper, nor fired twice. It waits for its inventory only outside the level's tick lock.
/// </summary>
[Collection(nameof(PluginTagFiles))]
public sealed class DispenserAtomicity
{
    private static readonly TimeSpan timeout = TimeSpan.FromSeconds(10);
    private static readonly Vector dispenser = new(8, 65, 8);

    [Fact]
    public async Task FiringWaitsForAClickAndNeverFiresTheItemTheClickTook()
    {
        await using var context = TestServerFactory.CreateServer($"dispenser-click-{Guid.NewGuid():N}");
        var world = context.World;
        var (state, container) = await PlaceDispenserAsync(world, arrows: 1);

        // A click holds the dispenser's inventory while the dispenser fires.
        var click = await container.Inventory.LockAsync();
        var firing = RedstoneDispenser.FireAsync(world, dispenser, state, dropper: false).AsTask();
        Assert.False(firing.IsCompleted);

        // The dispenser waits outside the level's tick lock, which the rest of the level can still take.
        var tickLockFree = await Task.Run(() =>
        {
            if (!Monitor.TryEnter(world.Fluids.TickLock))
                return false;

            Monitor.Exit(world.Fluids.TickLock);
            return true;
        });
        Assert.True(tickLockFree);

        // The click takes the arrow, then lets go: the dispenser finds nothing to fire.
        container.Inventory.SetItem(0, null);
        click.Dispose();
        await firing.WaitAsync(timeout);

        Assert.Equal(0, Arrows(world));
        Assert.Null(container.Inventory.GetItem(0));
    }

    [Fact]
    public async Task FiresAndConcurrentTakesSpendEachArrowOnce()
    {
        const int arrows = 64;

        await using var context = TestServerFactory.CreateServer($"dispenser-race-{Guid.NewGuid():N}");
        var world = context.World;
        var (state, container) = await PlaceDispenserAsync(world, arrows);

        // The level fires on its own path while a hopper-like taker empties the same slot from another thread.
        var access = container.GetItemAccess(side: null)!;
        var taken = 0;
        var taking = Task.Run(() =>
        {
            for (var attempt = 0; attempt < arrows; attempt++)
            {
                if (access.Extract(0, 1) is { Count: 1 })
                    Interlocked.Increment(ref taken);
            }
        });

        var firing = Task.Run(async () =>
        {
            for (var shot = 0; shot < arrows; shot++)
                await RedstoneDispenser.FireAsync(world, dispenser, state, dropper: false);
        });

        await Task.WhenAll(taking, firing).WaitAsync(timeout);

        var left = container.Inventory.GetItem(0)?.Count ?? 0;
        Assert.Equal(arrows, Arrows(world) + taken + left);
    }

    private static async Task<(IBlock State, ContainerBlockEntity Container)> PlaceDispenserAsync(
        WorldData.World world, int arrows)
    {
        WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(dispenser);

        var state = BlocksRegistry.Dispenser.WithProperty("facing", "east");
        world.BlockUpdates.SetBlock(dispenser, state, BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
        var container = Assert.IsAssignableFrom<ContainerBlockEntity>(world.GetResidentBlockEntity(dispenser));
        container.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Arrow, arrows));
        return (state, container);
    }

    private static int Arrows(WorldData.World world) =>
        world.GetEntitiesInRange((VectorD)dispenser, 16).OfType<Arrow>().Count();
}
