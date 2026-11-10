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
using System.IO;
using System.Diagnostics;
using System.Reflection;
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
    public async Task FiringAndRegionSaveCannotDeadlockEachOther()
    {
        const string probeFlag = "OBSIDIAN_DISPENSER_SAVE_PROBE";
        if (Environment.GetEnvironmentVariable(probeFlag) is string probeWorld)
        {
            await SaveRendezvousAsync(probeWorld);
            return;
        }

        // The original inversion cannot be cancelled: isolate it so a failed test can kill and join both blocked
        // operations instead of leaving a testhost holding world files or hanging server disposal.
        var worldName = $"dispenser-save-{Guid.NewGuid():N}";
        var directory = Path.GetDirectoryName(typeof(DispenserAtomicity).Assembly.Location)!;
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            WorkingDirectory = directory,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(DispenserAtomicity).Assembly.Location);
        start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName="
            + typeof(DispenserAtomicity).FullName + "." + nameof(FiringAndRegionSaveCannotDeadlockEachOther));
        start.Environment[probeFlag] = worldName;

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(35));
            }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                Assert.Fail("Dispenser/save rendezvous deadlocked: " + await stdout + await stderr);
            }

            Assert.True(process.ExitCode == 0, await stdout + await stderr);
        }
        finally
        {
            // Even a regressed child must release file handles before its uniquely named world is removed.
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            var worldPath = Path.Combine(directory, "worlds", worldName);
            if (Directory.Exists(worldPath))
                Directory.Delete(worldPath, recursive: true);
        }
    }

    private static async Task SaveRendezvousAsync(string worldName)
    {
        // Deliberately dispose only after success; the parent process contains and joins a deadlock on old code.
        var context = TestServerFactory.CreateServer(worldName);
        var world = context.World;
        var (state, container) = await PlaceDispenserAsync(world, arrows: 1);
        container.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Glowstone, 1));
        world.BlockUpdates.SetBlock(dispenser + Vector.East, BlocksRegistry.RespawnAnchor);
        var region = (Region)world.LoadRegionByChunk(0, 0);
        await region.InitAsync();

        using var snapshotEntered = new ManualResetEventSlim();
        using var continueSnapshot = new ManualResetEventSlim();
        var flushCalls = 0;
        var originalFlush = region.FlushLight;
        typeof(Region).GetProperty(nameof(Region.FlushLight), BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(region, (Action)(() =>
            {
                originalFlush?.Invoke();
                if (Interlocked.Increment(ref flushCalls) != 2)
                    return;

                // The second flush is inside SnapshotChunkAsync, with tick and the chunk lock held.
                snapshotEntered.Set();
                Assert.True(continueSnapshot.Wait(timeout));
            }));

        var click = await container.Inventory.LockAsync();
        var firingWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.EnqueueEntityAction(() =>
        {
            var pending = RedstoneDispenser.FireAsync(world, dispenser, state, dropper: false);
            firingWaiting.SetResult();
            return pending;
        });
        var firing = Task.Run(() => world.DoWorldTickAsync());
        await firingWaiting.Task.WaitAsync(timeout);
        var save = Task.Run(() => region.FlushAsync());
        Assert.True(snapshotEntered.Wait(timeout));

        // Fire is already queued on the inventory semaphore. Releasing the click admits it while the save owns
        // tick. Old code keeps this inventory and waits for tick; the save then waits for that same inventory.
        click.Dispose();
        continueSnapshot.Set();
        try
        {
            await Task.WhenAll(firing, save).WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"save completed={save.IsCompleted}, world tick completed={firing.IsCompleted}");
        }

        Assert.Null(container.Inventory.GetItem(0));
        Assert.Equal("1", world.BlockUpdates.GetBlock(dispenser + Vector.East).GetProperty("charges"));
        typeof(Region).GetProperty(nameof(Region.FlushLight), BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(region, originalFlush);
        await context.DisposeAsync();
    }

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
