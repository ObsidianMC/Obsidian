using System.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.Registries;
using Obsidian.Utilities;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The live block-change pipeline (R2, see docs/redstone/01-foundation.md): post-write visibility, shape updates on support removal
/// and wire connections, and bounded, coalesced, deterministic processing.
/// </summary>
public class BlockNotifications
{
    private const string Stone = "minecraft:stone";
    private const string Dirt = "minecraft:dirt";
    // This is a synthetic toggle target for the bounded-queue test. Redstone lamp now has a real R4 neighbor behavior.
    private const string Lamp = "minecraft:redstone_ore";

    [Fact]
    public async Task ReactionsSeeTheCommittedState()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 3, 62, 66, 0, 3);
        var seen = new List<(string SourceBefore, string SourceNow)>();
        world.Neighbors.Register(Dirt, new Recorder((change, level) =>
        {
            seen.Add((change.SourceState!.UnlocalizedName, level.GetLoadedBlock(change.Source)!.UnlocalizedName));
            return ValueTask.CompletedTask;
        }));

        await world.Place(new Vector(1, 64, 1), BlocksRegistry.Get(Dirt));
        await world.Place(new Vector(2, 64, 1), BlocksRegistry.Get(Stone));

        // The dirt next to the new stone sees the stone: the write came before the reaction.
        Assert.Contains((Stone, Stone), seen);
    }

    [Fact]
    public async Task RemovingSupportDropsAnAttachedTorch()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 2, 62, 66, 0, 2);
        var floor = new Vector(1, 63, 1);
        var torch = new Vector(1, 64, 1);
        await world.Place(floor, BlocksRegistry.Get(Stone));
        await world.Place(torch, BlocksRegistry.Get("minecraft:torch"));
        Assert.Equal("minecraft:torch", world.GetLoadedBlock(torch)!.UnlocalizedName);

        await world.Place(floor, BlocksRegistry.Air);

        Assert.True(world.GetLoadedBlock(torch)!.IsSameState(BlocksRegistry.Air));
    }

    [Fact]
    public async Task GapFillingWiresConnectImmediatelyInEveryOrder()
    {
        // After every placement, each wire must match the same set of wires placed in ascending order (vanilla's steady state).
        foreach (var order in new[] { new[] { 3, 5, 4 }, new[] { 4, 3, 5 }, new[] { 5, 4, 3 } })
        {
            var world = new RedstoneTestWorld();
            world.Load(0, 15, 62, 66, 0, 15);
            for (var x = 2; x <= 6; x++)
                await world.Place(new Vector(x, 63, 8), BlocksRegistry.Get(Stone));

            var placed = new List<int>();
            foreach (var x in order)
            {
                await world.Place(new Vector(x, 64, 8), BlocksRegistry.Get("minecraft:redstone_wire"));
                placed.Add(x);

                var reference = new RedstoneTestWorld();
                reference.Load(0, 15, 62, 66, 0, 15);
                for (var r = 2; r <= 6; r++)
                    await reference.Place(new Vector(r, 63, 8), BlocksRegistry.Get(Stone));
                foreach (var r in placed.OrderBy(v => v))
                    await reference.Place(new Vector(r, 64, 8), BlocksRegistry.Get("minecraft:redstone_wire"));

                foreach (var p in placed)
                {
                    var actual = world.GetLoadedBlock(new Vector(p, 64, 8))!;
                    var expected = reference.GetLoadedBlock(new Vector(p, 64, 8))!;
                    Assert.True(actual.IsSameState(expected),
                        $"order {string.Join(",", order)}: wire {p} differs from reference after placing {x}: " +
                        $"actual west={actual.GetProperty("west")} east={actual.GetProperty("east")} power={actual.GetProperty("power")}; " +
                        $"expected west={expected.GetProperty("west")} east={expected.GetProperty("east")} power={expected.GetProperty("power")}");
                }
            }
        }
    }

    [Fact]
    public async Task PlacingAWireNextToAWireReconnectsIt()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 3, 62, 66, 0, 2);
        const string wire = "minecraft:redstone_wire";
        await world.Place(new Vector(0, 63, 0), BlocksRegistry.Get(Stone));
        await world.Place(new Vector(1, 63, 0), BlocksRegistry.Get(Stone));
        await world.Place(new Vector(0, 64, 0), BlocksRegistry.Get(wire));
        Assert.Equal("none", world.GetLoadedBlock(new Vector(0, 64, 0))!.GetProperty("east"));

        await world.Place(new Vector(1, 64, 0), BlocksRegistry.Get(wire));

        Assert.Equal("side", world.GetLoadedBlock(new Vector(0, 64, 0))!.GetProperty("east"));
        // The newly placed wire must also derive its own west connection; updating only its already-existing neighbor is not enough.
        Assert.Equal("side", world.GetLoadedBlock(new Vector(1, 64, 0))!.GetProperty("west"));
    }

    [Fact]
    public async Task ChainedReactionsAreBoundedPerDrain()
    {
        // Two lamps that each toggle when the other changes: an endless chain that the per-drain limit must end.
        var world = new RedstoneTestWorld();
        world.Load(0, 3, 62, 66, 0, 2);
        await world.Place(new Vector(0, 64, 0), LampBlock(false));
        await world.Place(new Vector(1, 64, 0), LampBlock(false));
        var reactions = 0;
        world.Neighbors.Register(Lamp, new Recorder(async (change, level) =>
        {
            reactions++;
            var lamp = level.GetLoadedBlock(change.Position)!;
            await level.SetBlockAsync(change.Position, lamp.WithProperty("lit", lamp.GetProperty("lit") != "true"));
        }));

        await world.Place(new Vector(0, 64, 0), LampBlock(true));

        Assert.True(world.Pipeline.DroppedWork > 0);
        Assert.InRange(reactions, 1, BlockNotificationPipeline.MaxWorkPerDrain);
    }

    [Fact]
    public async Task ProcessingIsDeterministic()
    {
        var first = await RunScenarioAsync();
        var second = await RunScenarioAsync();

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task QueuedWorkWaitsForTheDrain()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 2, 62, 66, 0, 2);
        var reactions = 0;
        world.Neighbors.Register(Dirt, new Recorder((_, _) =>
        {
            reactions++;
            return ValueTask.CompletedTask;
        }));
        await world.Place(new Vector(1, 64, 1), BlocksRegistry.Get(Dirt));
        reactions = 0;

        // Like a fluid change under the fluid lock: the change is committed and queued, but nothing runs yet.
        world.Pipeline.Enqueue(world.Commit(new Vector(0, 64, 1), BlocksRegistry.Get(Stone)));
        Assert.Equal(0, reactions);

        await world.Pipeline.DrainAsync();
        Assert.Equal(1, reactions);
    }

    [Fact]
    public async Task PendingDuplicatesAreCoalesced()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 2, 62, 66, 0, 2);
        var reactions = 0;
        world.Neighbors.Register(Dirt, new Recorder((_, _) =>
        {
            reactions++;
            return ValueTask.CompletedTask;
        }));
        await world.Place(new Vector(1, 64, 1), BlocksRegistry.Get(Dirt));
        reactions = 0;

        var change = world.Commit(new Vector(0, 64, 1), BlocksRegistry.Get(Stone));
        world.Pipeline.Enqueue(change);
        world.Pipeline.Enqueue(change);
        await world.Pipeline.DrainAsync();

        Assert.Equal(1, reactions);
    }

    [Fact]
    public async Task UnloadedNeighborsAreNeitherReadNorReacted()
    {
        // Chunk 0 is loaded (x 0..15); x = 16 is in chunk 1, which isn't.
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var reactedTo = new List<Vector>();
        world.Neighbors.Register(Dirt, new Recorder((change, _) =>
        {
            reactedTo.Add(change.Position);
            return ValueTask.CompletedTask;
        }));
        await world.Place(new Vector(15, 64, 1), BlocksRegistry.Get(Dirt));

        await world.Place(new Vector(14, 64, 1), BlocksRegistry.Get(Stone));

        Assert.Equal([new Vector(15, 64, 1)], reactedTo);
        Assert.Null(world.GetLoadedBlock(new Vector(16, 64, 1)));
    }

    // Runs one scripted sequence and records every reaction in order: the same input must give the same record.
    private static async Task<List<string>> RunScenarioAsync()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 3, 62, 66, 0, 3);
        var log = new List<string>();
        world.Neighbors.Register(Dirt, new Recorder((change, level) =>
        {
            log.Add($"{change.Position}<-{change.Source}:{level.GetLoadedBlock(change.Source)!.UnlocalizedName}");
            return ValueTask.CompletedTask;
        }));
        await world.Place(new Vector(1, 64, 1), BlocksRegistry.Get(Dirt));
        await world.Place(new Vector(2, 64, 1), BlocksRegistry.Get(Stone));
        await world.Place(new Vector(2, 64, 1), BlocksRegistry.Air);
        await world.Place(new Vector(0, 64, 1), BlocksRegistry.Get(Dirt));
        return log;
    }

    private static IBlock LampBlock(bool lit) => BlocksRegistry.Get(Lamp).WithProperty("lit", lit);

    private sealed class Recorder(Func<NeighborChange, IBlockChangeWorld, ValueTask> onChange) : INeighborChangedBehavior
    {
        public ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world) => onChange(change, world);
    }
}
