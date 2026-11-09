using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.Registries;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Redstone;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>Obsidian's registration contracts, independent of gameplay rules.</summary>
public class BehaviorBindings
{
    [Fact]
    public void NeighborRegistrationsKeepPrecedenceInEitherOrder()
    {
        foreach (var classFirst in new[] { false, true })
        {
            var bindings = new NeighborBehaviors();
            var specific = new Behavior();
            var general = new Behavior();
            if (classFirst)
                bindings.RegisterClass("FenceBlock", general);

            bindings.Register(BlocksRegistry.OakFence.RegistryId, specific);
            if (!classFirst)
                bindings.RegisterClass("FenceBlock", general);

            Assert.True(bindings.TryGet(BlocksRegistry.OakFence.WithProperty("east", true), out var found));
            Assert.Same(specific, found);
            Assert.True(bindings.TryGet(BlocksRegistry.BirchFence, out found));
            Assert.Same(general, found);
            Assert.False(bindings.TryGet(BlocksRegistry.Stone, out _));

            Assert.Equal("A neighbor behavior is already registered for minecraft:oak_fence.",
                Assert.Throws<InvalidOperationException>(() => bindings.Register("minecraft:oak_fence", general)).Message);
            Assert.Equal("A neighbor behavior is already registered for FenceBlock.",
                Assert.Throws<InvalidOperationException>(() => bindings.RegisterClass("FenceBlock", specific)).Message);
            Assert.True(bindings.TryGet(BlocksRegistry.OakFence, out found));
            Assert.Same(specific, found);

            // The old dictionary keys are exact: accepting a registry alias here would change their contract.
            var exact = new NeighborBehaviors();
            exact.Register("MINECRAFT:STONE", specific);
            exact.RegisterClass("block", general);
            Assert.False(exact.TryGet(BlocksRegistry.Stone, out _));
        }
    }

    [Fact]
    public void RedstoneClassRegistrationsExpandAndKeepDuplicateErrors()
    {
        var bindings = new RedstoneBehaviors();
        var behavior = new Behavior();
        var rejected = new Behavior();
        bindings.Register("FenceBlock", behavior);
        bindings.RegisterContact("FenceBlock", behavior);
        Assert.Same(behavior, bindings.For(BlocksRegistry.OakFence));
        Assert.Same(behavior, bindings.For(BlocksRegistry.BirchFence.WithProperty("west", true)));
        Assert.Same(behavior, bindings.ContactFor(BlocksRegistry.BirchFence));
        Assert.NotSame(behavior, bindings.For(BlocksRegistry.Stone));
        Assert.Null(bindings.ContactFor(BlocksRegistry.Stone));
        Assert.Equal("A redstone behavior is already registered for FenceBlock.",
            Assert.Throws<InvalidOperationException>(() => bindings.Register("FenceBlock", rejected)).Message);
        Assert.Equal("A redstone contact is already registered for FenceBlock.",
            Assert.Throws<InvalidOperationException>(() => bindings.RegisterContact("FenceBlock", rejected)).Message);
        Assert.Same(behavior, bindings.For(BlocksRegistry.OakFence));
        Assert.Same(behavior, bindings.ContactFor(BlocksRegistry.OakFence));
    }

    [Fact]
    public void PluginBlocksRegisteredAfterDispatchersKeepNamedAndFallbackBindings()
    {
        const string name = "behavior_bindings:late_block";
        var neighbors = new NeighborBehaviors();
        var fallback = new Behavior();
        var redstone = new RedstoneBehaviors(fallback);
        var specific = new Behavior();
        // Register the name even before the block exists, as the original dictionaries allow.
        neighbors.Register(name, specific);
        neighbors.RegisterPlaced(name, specific);
        neighbors.RegisterRemoved(name, specific);
        var type = BlocksRegistry.Register(name, new BlockProperties { StateProperties = [StateProperty.Bool("lit")] });
        var state = type.DefaultState.WithProperty("lit", true);
        Assert.True(state.StateId() >= BlocksRegistry.StateToNumeric.Length);
        Assert.True(neighbors.TryGet(state, out var found));
        Assert.Same(specific, found);
        Assert.True(neighbors.TryGetPlaced(state, out var placed));
        Assert.Same(specific, placed);
        Assert.True(neighbors.TryGetRemoved(state, out var removed));
        Assert.Same(specific, removed);
        Assert.Same(fallback, redstone.For(state));
        Assert.Null(redstone.ContactFor(state));
        Assert.Equal("Block", state.BlockClass());
        Assert.Equal(BlockClassId.Block, state.ClassId());
        Assert.False(state.IsSignalSource());

        // A later generic-class registration still applies to plugin blocks; it takes priority over the fallback.
        redstone.Register("Block", specific);
        redstone.RegisterContact("Block", specific);
        Assert.Same(specific, redstone.For(state));
        Assert.Same(specific, redstone.ContactFor(state));
        var generic = new NeighborBehaviors();
        generic.RegisterClass("Block", specific);
        Assert.True(generic.TryGet(state, out found));
        Assert.Same(specific, found);
    }

    private sealed class Behavior : INeighborChangedBehavior, IPlacementBehavior, IRemovalBehavior,
        IRedstoneBehavior, IRedstoneContact
    {
        public ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world) => ValueTask.CompletedTask;
        public ValueTask OnPlacedAsync(BlockChange change, IBlockChangeWorld world) => ValueTask.CompletedTask;
        public ValueTask OnRemovedAsync(BlockChange change, IBlockChangeWorld world) => ValueTask.CompletedTask;
        public ValueTask EntityInsideAsync(IRedstoneLevel level, Vector position, IBlock state) => ValueTask.CompletedTask;
    }
}
