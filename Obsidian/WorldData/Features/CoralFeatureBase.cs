using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// Shared logic of vanilla's CoralFeature: picks a random coral block and builds a shape out of it, decorating each coral
/// block with corals, sea pickles and wall fans.
/// </summary>
/// <remarks>
/// Vanilla picks from the <c>coral_blocks</c>, <c>corals</c> and <c>wall_corals</c> tags with <c>nextInt(size)</c>, so the
/// tag order matters; the generated tags keep vanilla's resolved order.
/// </remarks>
public abstract class CoralFeatureBase : ConfiguredFeatureBase
{
    private static readonly BlockSet coralsTag = new("#minecraft:corals");

    private static IBlock[] CoralBlocks => field ??= Resolve(TagsRegistry.Block.CoralBlocks);

    private static IBlock[] Corals => field ??= Resolve(TagsRegistry.Block.Corals);

    private static IBlock[] WallCorals => field ??= Resolve(TagsRegistry.Block.WallCorals);

    private static IBlock SeaPickle => field ??= BlocksRegistry.Get(Material.SeaPickle);

    public sealed override bool Place(FeatureContext context)
    {
        if (!context.Level.EnsureCanWrite(context.Origin))
            return false;

        var coral = CoralBlocks[context.Random.NextInt(CoralBlocks.Length)];
        return this.PlaceShape(context.Level, context.Random, context.Origin, coral);
    }

    /// <summary>
    /// Builds the coral shape from <paramref name="coral"/> blocks.
    /// </summary>
    protected abstract bool PlaceShape(IWorldGenLevel level, IRandomSource random, Vector origin, IBlock coral);

    /// <summary>
    /// Vanilla <c>CoralFeature.placeCoralBlock</c>: places a coral block where there's water (or coral) with water above, and
    /// may top it with a coral or sea pickle and attach wall fans.
    /// </summary>
    protected static bool PlaceCoralBlock(IWorldGenLevel level, IRandomSource random, Vector position, IBlock coral)
    {
        var above = position + Vector.Up;
        var existing = level.GetBlock(position);
        if (existing.Material != Material.Water && !coralsTag.Contains(existing) || level.GetBlock(above).Material != Material.Water)
            return false;

        level.SetBlock(position, coral);

        if (random.NextFloat() < 0.25f)
            level.SetBlock(above, Corals[random.NextInt(Corals.Length)]);
        else if (random.NextFloat() < 0.05f)
            level.SetBlock(above, SeaPickle.WithProperty("pickles", random.NextInt(4) + 1));

        foreach (var face in FeatureHelpers.Horizontal)
        {
            if (random.NextFloat() >= 0.2f)
                continue;

            var side = position.Offset(face);
            if (level.GetBlock(side).Material != Material.Water)
                continue;

            var fan = WallCorals[random.NextInt(WallCorals.Length)];
            if (fan.HasProperty("facing"))
                fan = fan.WithProperty("facing", FeatureHelpers.FaceName(face));

            level.SetBlock(side, fan);
        }

        return true;
    }

    private static IBlock[] Resolve(Tag tag) => [.. tag.Entries.Select(id => BlocksRegistry.Get((Material)id))];
}
