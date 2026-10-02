using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// Places a multiface block (glow lichen, sculk vein) on a nearby supporting surface and may spread it once, like vanilla's
/// MultifaceGrowthFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:multiface_growth")]
public sealed class MultifaceGrowthFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:multiface_growth";

    /// <summary>
    /// The multiface block id (defaults to glow lichen).
    /// </summary>
    public string Block { get; init; } = "minecraft:glow_lichen";

    public int SearchRange { get; init; } = 10;

    public bool CanPlaceOnFloor { get; init; }

    public bool CanPlaceOnCeiling { get; init; }

    public bool CanPlaceOnWall { get; init; }

    public float ChanceOfSpreading { get; init; } = 0.5f;

    public required BlockSet CanBePlacedOn { get; init; }

    private MultifaceSpreader Spreader => field ??= MultifaceSpreader.For(BlocksRegistry.Get(this.Block));

    // Vanilla order: up (ceiling), down (floor), then the horizontal plane.
    private BlockFace[] ValidDirections => field ??= this.BuildValidDirections();

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin) || !IsAirOrWater(level.GetBlock(origin)))
            return false;

        var validDirections = this.ValidDirections;
        Span<BlockFace> directions = stackalloc BlockFace[validDirections.Length];
        validDirections.CopyTo(directions);
        FeatureHelpers.Shuffle(directions, random);
        if (this.PlaceGrowthIfPossible(level, origin, level.GetBlock(origin), random, directions))
            return true;

        Span<BlockFace> others = stackalloc BlockFace[validDirections.Length];
        foreach (var direction in directions)
        {
            var otherCount = 0;
            foreach (var face in validDirections)
            {
                if (face != direction.Opposite())
                    others[otherCount++] = face;
            }

            FeatureHelpers.Shuffle(others[..otherCount], random);

            for (var i = 0; i < this.SearchRange; i++)
            {
                // Vanilla resets to origin + direction on every step (setWithOffset), so the search never moves further out.
                var position = origin.Offset(direction);
                var state = level.GetBlock(position);
                if (!IsAirOrWater(state) && state.RegistryId != this.Spreader.Block.RegistryId)
                    break;

                if (this.PlaceGrowthIfPossible(level, position, state, random, others[..otherCount]))
                    return true;
            }
        }

        return false;
    }

    private bool PlaceGrowthIfPossible(IWorldGenLevel level, Vector position, IBlock state, IRandomSource random, ReadOnlySpan<BlockFace> directions)
    {
        foreach (var direction in directions)
        {
            if (!this.CanBePlacedOn.Contains(level.GetBlock(position.Offset(direction))))
                continue;

            var newState = this.Spreader.GetStateForPlacement(state, level, position, direction);
            if (newState is null)
                return false;

            level.SetBlock(position, newState);
            if (random.NextFloat() < this.ChanceOfSpreading)
                this.Spreader.SpreadFromFaceTowardRandomDirection(newState, level, position, direction, random);

            return true;
        }

        return false;
    }

    private BlockFace[] BuildValidDirections()
    {
        var directions = new List<BlockFace>(6);
        if (this.CanPlaceOnCeiling)
            directions.Add(BlockFace.Up);

        if (this.CanPlaceOnFloor)
            directions.Add(BlockFace.Down);

        if (this.CanPlaceOnWall)
            directions.AddRange(FeatureHelpers.Horizontal);

        return [.. directions];
    }

    private static bool IsAirOrWater(IBlock block) => block.IsAir || block.Material == Material.Water;
}
