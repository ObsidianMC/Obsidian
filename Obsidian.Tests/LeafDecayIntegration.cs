using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Leaf decay, vanilla <c>LeavesBlock</c>. The rules are checked against the vanilla bytecode; the live-world tests call
/// the same entry points the level tick uses.
/// </summary>
public class LeafDecayIntegration
{
    private static readonly Vector Spot = BlockBreakIntegration.Spot;

    private static IBlock Leaves(int distance, bool persistent = false) =>
        BlockStateProperties.GetState("minecraft:oak_leaves").WithProperty("distance", distance).WithProperty("persistent", persistent);

    private static async Task<int> DistanceAt(AbstractLevel world, Vector position) =>
        int.Parse((await world.GetBlockAsync(position))!.GetProperty("distance")!);

    [Fact]
    public void SchedulingFollowsVanillaUpdateShape()
    {
        var log = BlockStateProperties.GetState("minecraft:oak_log");
        var air = BlocksRegistry.Air;

        // Any change that isn't a log gives distance 8, which never equals 1, so vanilla always schedules.
        Assert.True(LeafDecay.ShouldScheduleTick(Leaves(7), air));
        // A log next to a leaf at distance 1 changes nothing.
        Assert.False(LeafDecay.ShouldScheduleTick(Leaves(1), log));
        // A log next to a leaf at any other distance schedules the correction.
        Assert.True(LeafDecay.ShouldScheduleTick(Leaves(7), log));
    }

    [Fact]
    public void DecayNeedsDistanceSevenAndNotPersistent()
    {
        Assert.True(LeafDecay.IsDecaying(Leaves(7)));
        Assert.False(LeafDecay.IsDecaying(Leaves(6)));
        Assert.False(LeafDecay.IsDecaying(Leaves(7, persistent: true)));
        Assert.False(LeafDecay.IsDecaying(BlockStateProperties.GetState("minecraft:stone")));
    }

    [Fact]
    public async Task IsolatedLeavesDecayOnTheirRandomTick()
    {
        await using var context = TestServerFactory.CreateServer("leaves-decay");
        var world = (AbstractLevel)context.World;
        await world.SetBlockAsync(Spot, Leaves(7), true);

        await LeafDecay.RandomTickAsync(world, Spot);

        Assert.True((await world.GetBlockAsync(Spot))!.IsAir);
    }

    [Fact]
    public async Task PersistentLeavesDoNotDecay()
    {
        await using var context = TestServerFactory.CreateServer("leaves-persistent");
        var world = (AbstractLevel)context.World;
        await world.SetBlockAsync(Spot, Leaves(7, persistent: true), true);

        await LeafDecay.RandomTickAsync(world, Spot);

        Assert.Equal("minecraft:oak_leaves", (await world.GetBlockAsync(Spot))!.UnlocalizedName);
    }

    [Fact]
    public async Task LeavesNextToALogDoNotDecayAndTheirTickKeepsTheDistance()
    {
        await using var context = TestServerFactory.CreateServer("leaves-log");
        var world = (AbstractLevel)context.World;
        await world.SetBlockAsync(Spot + Vector.East, BlockStateProperties.GetState("minecraft:oak_log"), true);
        await world.SetBlockAsync(Spot, Leaves(1), true);

        await LeafDecay.RandomTickAsync(world, Spot);
        await LeafDecay.TickAsync(world, Spot);

        Assert.Equal(1, await DistanceAt(world, Spot));
    }

    [Fact]
    public async Task TheScheduledTickRecomputesADistanceFromANearbyLog()
    {
        await using var context = TestServerFactory.CreateServer("leaves-tick");
        var world = (AbstractLevel)context.World;
        await world.SetBlockAsync(Spot + Vector.East, BlockStateProperties.GetState("minecraft:oak_log"), true);
        await world.SetBlockAsync(Spot, Leaves(7), true);

        await LeafDecay.TickAsync(world, Spot);

        Assert.Equal(1, await DistanceAt(world, Spot));
    }

    [Fact]
    public async Task LosingTheLogSchedulesALeafTickThatLeavesItDecayable()
    {
        await using var context = TestServerFactory.CreateServer("leaves-lost-log");
        var world = (AbstractLevel)context.World;
        await world.SetBlockAsync(Spot + Vector.East, BlockStateProperties.GetState("minecraft:oak_log"), true);
        await world.SetBlockAsync(Spot, Leaves(1), true);

        // Removing the log is a live change: the leaf's shape update schedules its distance tick.
        await world.SetBlockAsync(Spot + Vector.East, BlocksRegistry.Air, true);
        var region = (Region)world.GetRegionForLocation(new VectorD(Spot.X + 0.5, Spot.Y + 0.5, Spot.Z + 0.5))!;
        await region.TickBlocksAsync();

        Assert.Equal(7, await DistanceAt(world, Spot));
        await LeafDecay.RandomTickAsync(world, Spot);
        Assert.True((await world.GetBlockAsync(Spot))!.IsAir);
    }
}
