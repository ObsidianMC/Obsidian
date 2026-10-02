namespace Obsidian.WorldData.Features.Tree;

/// <summary>
/// Block tests shared by tree placement (vanilla <c>TreeFeature</c>/<c>Feature</c> helpers and the block tags they use).
/// </summary>
internal static class TreeBlocks
{
    public static readonly BlockSet Leaves = new("#minecraft:leaves");

    public static readonly BlockSet Logs = new("#minecraft:logs");

    public static readonly BlockSet Dirt = new("#minecraft:dirt");

    public static readonly BlockSet Snow = new("#minecraft:snow");

    private static readonly BlockSet replaceableByTrees = new("#minecraft:replaceable_by_trees");

    private static readonly BlockSet grassOrMycelium = new("minecraft:grass_block", "minecraft:mycelium");

    private static readonly BlockSet vine = new("minecraft:vine");

    // OptionalLeafDistance of each state plus 2 (1 for none), indexed by state id and computed on first use; 0 until then.
    private static readonly byte[] leafDistances = new byte[BlocksRegistry.StateToNumeric.Length];

    /// <summary>Vanilla <c>TreeFeature.validTreePos</c>: air or <c>#replaceable_by_trees</c>.</summary>
    public static bool ValidTreePos(IWorldGenLevel level, Vector position)
    {
        var block = level.GetBlock(position);
        return block.IsAir || replaceableByTrees.Contains(block);
    }

    /// <summary>Vanilla <c>TreeFeature.isAirOrLeaves</c>.</summary>
    public static bool IsAirOrLeaves(IWorldGenLevel level, Vector position)
    {
        var block = level.GetBlock(position);
        return block.IsAir || Leaves.Contains(block);
    }

    /// <summary>Vanilla <c>TreeFeature.isVine</c>.</summary>
    public static bool IsVine(IWorldGenLevel level, Vector position) => vine.Contains(level.GetBlock(position));

    public static bool IsVine(IBlock block) => vine.Contains(block);

    /// <summary>Vanilla <c>Feature.isGrassOrDirt</c> (despite the name, just <c>#dirt</c>).</summary>
    public static bool IsGrassOrDirt(IWorldGenLevel level, Vector position) => Dirt.Contains(level.GetBlock(position));

    /// <summary>Vanilla <c>TrunkPlacer.isDirt</c>: <c>#dirt</c> except grass blocks and mycelium.</summary>
    public static bool IsDirtUnderTrunk(IBlock block) => Dirt.Contains(block) && !grassOrMycelium.Contains(block);

    /// <summary>Vanilla <c>FluidState.isSourceOfType(Fluids.WATER)</c>.</summary>
    public static bool IsWaterSource(IBlock block) => block.GetFluid() == FluidKind.Water;

    /// <summary>Vanilla <c>FluidState.is(FluidTags.WATER)</c>: still or flowing water.</summary>
    public static bool IsWater(IBlock block) => block.GetFluid() is FluidKind.Water or FluidKind.FlowingWater;

    /// <inheritdoc cref="OptionalLeafDistance(IBlock)"/>
    public static int? OptionalLeafDistance(int stateId)
    {
        // Threads racing on an entry store the same value.
        var entry = leafDistances[stateId];
        if (entry == 0)
            leafDistances[stateId] = entry = (byte)((OptionalLeafDistance(BlocksRegistry.Get(stateId)) ?? -1) + 2);

        return entry == 1 ? null : entry - 2;
    }

    /// <summary>Vanilla <c>LeavesBlock.getOptionalDistanceAt</c>: 0 for logs, the <c>distance</c> of leaves, otherwise none.</summary>
    public static int? OptionalLeafDistance(IBlock block)
    {
        if (Logs.Contains(block))
            return 0;

        if (!Leaves.Contains(block))
            return null;

        var distance = block.GetProperty("distance");
        return distance is null ? null : int.Parse(distance);
    }

    /// <summary>
    /// Approximation of vanilla <c>BlockState.isSolidRender()</c> (an occluding full cube), which the block physics
    /// data doesn't include: full collision cubes except leaves and the glass-like/non-occluding block classes.
    /// </summary>
    public static bool IsSolidRender(IBlock block)
    {
        if (!block.IsCollisionShapeFullBlock() || Leaves.Contains(block))
            return false;

        return block.BlockClass() is not ("TransparentBlock" or "StainedGlassBlock" or "TintedGlassBlock" or "IceBlock"
            or "FrostedIceBlock" or "HalfTransparentBlock" or "WaterloggedTransparentBlock" or "WeatheringCopperGrateBlock"
            or "MangroveRootsBlock" or "SpawnerBlock" or "TrialSpawnerBlock" or "VaultBlock" or "BarrierBlock"
            or "BeaconBlock" or "SlimeBlock" or "HoneyBlock");
    }
}
