using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.Registries;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Redstone;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>Obsidian's registration contracts and block metadata bindings against captured vanilla data.</summary>
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

            // Register the block and its class in both possible orders.
            if (classFirst)
                bindings.RegisterClass("FenceBlock", general);

            bindings.Register(BlocksRegistry.OakFence.RegistryId, specific);

            if (!classFirst)
                bindings.RegisterClass("FenceBlock", general);

            var connectedOak = BlocksRegistry.OakFence.WithProperty("east", true);
            Assert.True(bindings.TryGet(connectedOak, out var found));
            Assert.Same(specific, found);
            Assert.True(bindings.TryGet(BlocksRegistry.BirchFence, out found));
            Assert.Same(general, found);
            Assert.False(bindings.TryGet(BlocksRegistry.Stone, out _));

            // Rejected duplicates leave the original registration in place.
            Assert.Throws<InvalidOperationException>(() => bindings.Register("minecraft:oak_fence", general));
            Assert.Throws<InvalidOperationException>(() => bindings.RegisterClass("FenceBlock", specific));
            Assert.True(bindings.TryGet(BlocksRegistry.OakFence, out found));
            Assert.Same(specific, found);

            // The old dictionary keys are exact; registry aliases must not broaden them.
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
        var connectedBirch = BlocksRegistry.BirchFence.WithProperty("west", true);

        bindings.Register("FenceBlock", behavior);

        Assert.Same(behavior, bindings.For(BlocksRegistry.OakFence));
        Assert.Same(behavior, bindings.For(connectedBirch));
        Assert.NotSame(behavior, bindings.For(BlocksRegistry.Stone));

        // A duplicate must not replace the signal behavior.
        Assert.Throws<InvalidOperationException>(() => bindings.Register("FenceBlock", rejected));
        Assert.Same(behavior, bindings.For(BlocksRegistry.OakFence));
    }

    [Fact]
    public void PluginBlocksCreatedAfterDispatchersKeepNamedAndFallbackBindings()
    {
        const string name = "behavior_bindings:late_block";
        var neighbors = new NeighborBehaviors();
        var fallback = new Behavior();
        var redstone = new RedstoneBehaviors(fallback);
        var specific = new Behavior();
        var properties = new BlockProperties { StateProperties = [StateProperty.Bool("lit")] };

        // Register the name before the block exists, as the original dictionaries allow.
        neighbors.Register(name, specific);
        neighbors.RegisterPlaced(name, specific);
        neighbors.RegisterRemoved(name, specific);

        // These lookups need a plugin block's name and ids, not a mutation of the process-wide registry.
        var type = new BlockType(
            id: BlocksRegistry.ResourceIds.Length,
            firstStateId: BlocksRegistry.StateToNumeric.Length,
            identifier: name,
            properties: properties.StateProperties,
            defaultState: properties.DefaultState);
        var state = type.With(type.DefaultState, "lit", "true");

        Assert.True(state.StateId() >= BlocksRegistry.StateToNumeric.Length);
        Assert.True(neighbors.TryGet(state, out var found));
        Assert.Same(specific, found);

        Assert.True(neighbors.TryGetPlaced(state, out var placed));
        Assert.Same(specific, placed);
        Assert.True(neighbors.TryGetRemoved(state, out var removed));
        Assert.Same(specific, removed);

        Assert.Same(fallback, redstone.For(state));
        Assert.Equal("Block", state.BlockClass());
        Assert.Equal(BlockClassId.Block, state.ClassId());
        Assert.False(state.IsSignalSource());

        // A later generic class registration still applies to plugin blocks and precedes the fallback.
        redstone.Register("Block", specific);

        Assert.Same(specific, redstone.For(state));

        var generic = new NeighborBehaviors();

        generic.RegisterClass("Block", specific);

        Assert.True(generic.TryGet(state, out found));
        Assert.Same(specific, found);
    }

    [Fact]
    public void BlockMetadataBindingsMatchCapturedPhysics()
    {
        using var physicsStream = typeof(BlocksRegistry).Assembly
            .GetManifestResourceStream("Obsidian.Assets.block_physics.json")!;
        using var physics = JsonDocument.Parse(physicsStream);
        using var blocksStream = typeof(BlocksRegistry).Assembly
            .GetManifestResourceStream("Obsidian.Assets.blocks.json")!;
        using var blocks = JsonDocument.Parse(blocksStream);

        var capturedClasses = physics.RootElement.GetProperty("blockClasses");
        var capturedTypes = physics.RootElement.GetProperty("blockEntityTypes");
        var capturedSources = physics.RootElement.GetProperty("signalSources")
            .EnumerateArray()
            .Select(entry => entry.GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        var mismatches = new List<string>();
        var idsByClass = new Dictionary<string, BlockClassId>(StringComparer.Ordinal);
        var classesById = new Dictionary<BlockClassId, string>();

        // Compare each block's public lookup results with its captured metadata.
        foreach (var capturedBlock in blocks.RootElement.EnumerateObject())
        {
            var name = capturedBlock.Name;
            var block = BlocksRegistry.Get(name);
            var expectedClass = capturedClasses.GetProperty(name).GetString()!;
            var actualClassId = block.ClassId();

            CompareCapturedValue(mismatches, name, "BlockClass", expectedClass, block.BlockClass());
            CompareClassIdentity(mismatches, name, expectedClass, actualClassId, idsByClass, classesById);

            var expectedType = capturedTypes.TryGetProperty(name, out var capturedType)
                ? capturedType.GetString() : null;
            CompareCapturedValue(mismatches, name, "BlockEntityType", expectedType, block.BlockEntityType());

            // State ids come from the jar's block report, independently of Obsidian's generated id tables.
            var expectedSource = capturedSources.Contains(name);
            foreach (var capturedState in capturedBlock.Value.GetProperty("states").EnumerateArray())
            {
                var stateId = capturedState.GetProperty("id").GetInt32();
                var state = BlocksRegistry.Get(stateId);
                var field = $"IsSignalSource at state {stateId}";

                CompareCapturedValue(mismatches, name, field, expectedSource, state.IsSignalSource());
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    private static void CompareClassIdentity(
        List<string> mismatches,
        string blockName,
        string capturedClass,
        BlockClassId actualId,
        Dictionary<string, BlockClassId> idsByClass,
        Dictionary<BlockClassId, string> classesById)
    {
        // Named classes must resolve to their matching member; other classes need distinct, unnamed ids.
        if (Enum.TryParse<BlockClassId>(capturedClass, out var namedId))
            CompareCapturedValue(mismatches, blockName, "ClassId", namedId, actualId);
        else if (Enum.IsDefined(actualId))
            mismatches.Add($"{blockName}: class {capturedClass} incorrectly resolves to named id {actualId}.");

        if (idsByClass.TryGetValue(capturedClass, out var previousId))
            CompareCapturedValue(mismatches, blockName, $"ClassId for {capturedClass}", previousId, actualId);
        else
            idsByClass.Add(capturedClass, actualId);

        if (classesById.TryGetValue(actualId, out var previousClass) && previousClass != capturedClass)
            mismatches.Add($"{blockName}: classes {previousClass} and {capturedClass} share ClassId {actualId}.");
        else
            classesById.TryAdd(actualId, capturedClass);
    }

    private static void CompareCapturedValue<T>(
        List<string> mismatches, string blockName, string field, T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            mismatches.Add($"{blockName}: {field} expected '{expected}', got '{actual}'.");
    }

    private sealed class Behavior : INeighborChangedBehavior, IPlacementBehavior, IRemovalBehavior,
        IRedstoneBehavior
    {
        public ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world) =>
            ValueTask.CompletedTask;

        public ValueTask OnPlacedAsync(BlockChange change, IBlockChangeWorld world) => ValueTask.CompletedTask;

        public ValueTask OnRemovedAsync(BlockChange change, IBlockChangeWorld world) => ValueTask.CompletedTask;
    }
}
