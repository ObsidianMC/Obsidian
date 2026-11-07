using Obsidian.API.Blocks;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators;
using static Obsidian.WorldData.Placement.PlaceContexts;

namespace Obsidian.WorldData.Placement;

internal static partial class BlockPlacements
{
    /// <summary>
    /// Vanilla <c>DoublePlantBlock</c>: the lower half of a two-block plant, with room above for the upper half.
    /// </summary>
    public static IBlock? DoublePlant(BlockPlaceContext context)
    {
        var position = context.Position;
        var belowTop = context.Level.Level is AbstractLevel live && !live.IsOutsideBuildHeight(position.Y + 1);
        if (!belowTop || !context.Level.GetBlock(position + Vector.Up).CanBeReplaced())
            return null;

        return context.Block;
    }

    /// <summary>
    /// Vanilla <c>SmallDripleafBlock</c>: a two-block plant facing away from the player, waterlogged in any water.
    /// </summary>
    public static IBlock? SmallDripleaf(BlockPlaceContext context)
    {
        if (DoublePlant(context) is not IBlock state)
            return null;

        return state
            .WithProperty("facing", Name(context.HorizontalDirection.Opposite()))
            .WithProperty("waterlogged", IsWater(context.Replaced()));
    }

    /// <summary>Vanilla <c>TallSeagrassBlock</c>: a two-block plant with full water above it.</summary>
    public static IBlock? TallSeagrass(BlockPlaceContext context)
    {
        if (DoublePlant(context) is not IBlock state)
            return null;

        var above = context.Level.GetBlock(context.Position + Vector.Up);
        return IsFullWater(above) ? state : null;
    }

    /// <summary>Vanilla <c>BigDripleafBlock</c>: facing the dripleaf it grows on, or away from the player.</summary>
    public static IBlock BigDripleaf(BlockPlaceContext context)
    {
        var below = context.Level.GetBlock(context.Position + Vector.Down);
        var onDripleaf = below.RegistryId is BlockIds.BigDripleaf or BlockIds.BigDripleafStem;
        var facing = onDripleaf ? below.GetProperty("facing")! : Name(context.HorizontalDirection.Opposite());
        return context.Block
            .WithProperty("waterlogged", context.Replaced().IsFluidSource() && IsWater(context.Replaced()))
            .WithProperty("facing", facing);
    }

    /// <summary>Vanilla <c>MangrovePropaguleBlock</c>: fully grown, taking the water it replaces.</summary>
    public static IBlock MangrovePropagule(BlockPlaceContext context) =>
        context.Block.WaterloggedFor(context).WithProperty("age", 4);

    /// <summary>
    /// Vanilla <c>GrowingPlantHeadBlock</c> (weeping, twisting and cave vines): the plant's body when it continues one,
    /// otherwise a new tip of a random age.
    /// </summary>
    public static IBlock GrowingPlant(BlockPlaceContext context)
    {
        var (growth, body) = context.Block.RegistryId switch
        {
            BlockIds.WeepingVines => (BlockFace.Down, BlockIds.WeepingVinesPlant),
            BlockIds.TwistingVines => (BlockFace.Up, BlockIds.TwistingVinesPlant),
            BlockIds.CaveVines => (BlockFace.Down, BlockIds.CaveVinesPlant),
            _ => throw new ArgumentException($"{context.Block.UnlocalizedName} isn't a growing plant.", nameof(context))
        };

        var ahead = context.Level.GetBlock(context.Position.Offset(growth));
        if (ahead.RegistryId == context.Block.RegistryId || ahead.RegistryId == body)
            return BlocksRegistry.GetDefault(body);

        // Vanilla GrowingPlantHeadBlock.getStateForPlacement(random): a random age below 25.
        return context.Block.WithProperty("age", context.Level.Random.NextInt(25));
    }

    /// <summary>
    /// Vanilla <c>CandleBlock</c>: another candle in a candle of its kind, otherwise one candle taking the water there.
    /// </summary>
    public static IBlock Candle(BlockPlaceContext context)
    {
        var replaced = context.Replaced();
        if (replaced.RegistryId == context.Block.RegistryId)
            return Grown(replaced, "candles", 4);

        return context.Block.WaterloggedFor(context);
    }

    /// <summary>Vanilla <c>SeaPickleBlock</c>: another pickle in a sea pickle, otherwise one taking the water.</summary>
    public static IBlock SeaPickle(BlockPlaceContext context)
    {
        var replaced = context.Replaced();
        if (replaced.RegistryId == context.Block.RegistryId)
            return Grown(replaced, "pickles", 4);

        return context.Block.WaterloggedFor(context);
    }

    /// <summary>Vanilla <c>TurtleEggBlock</c>: another egg in a clutch, otherwise one egg.</summary>
    public static IBlock TurtleEgg(BlockPlaceContext context)
    {
        var replaced = context.Replaced();
        return replaced.RegistryId == context.Block.RegistryId ? Grown(replaced, "eggs", 4) : context.Block;
    }

    /// <summary>Vanilla <c>SnowLayerBlock</c>: another layer on a snow layer, otherwise one layer.</summary>
    public static IBlock SnowLayer(BlockPlaceContext context)
    {
        var replaced = context.Replaced();
        return replaced.RegistryId == context.Block.RegistryId ? Grown(replaced, "layers", 8) : context.Block;
    }

    /// <summary>
    /// Vanilla <c>SegmentableBlock</c> (flower beds and leaf litter): another segment in a patch of its kind, otherwise
    /// one segment facing away from the player.
    /// </summary>
    public static IBlock Segmented(BlockPlaceContext context)
    {
        var replaced = context.Replaced();
        var property = context.Block.HasProperty("flower_amount") ? "flower_amount" : "segment_amount";
        if (replaced.RegistryId == context.Block.RegistryId)
            return Grown(replaced, property, 4);

        return context.Block.WithProperty("facing", Name(context.HorizontalDirection.Opposite()));
    }

    // The block with one more of what it counts, up to the most it holds.
    private static IBlock Grown(IBlock block, string property, int most)
    {
        var count = int.Parse(block.GetProperty(property)!);
        return block.WithProperty(property, Math.Min(most, count + 1));
    }

    /// <summary>Vanilla <c>HangingRootsBlock</c>: taking the water it replaces.</summary>
    public static IBlock HangingRoots(BlockPlaceContext context) => context.Block.WaterloggedFor(context);

    /// <summary>
    /// Vanilla <c>MossyCarpetBlock</c>: a base carpet, with sides where it hangs against walls. Where nothing holds it,
    /// it isn't placed.
    /// </summary>
    public static IBlock? MossyCarpet(BlockPlaceContext context)
    {
        var shaped = Generators.Features.FeatureRegistry.UpdateMossCarpetShape(
            context.Block,
            View(context),
            context.Position);
        return shaped.IsAir ? null : shaped;
    }
}
