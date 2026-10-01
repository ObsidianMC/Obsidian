namespace Obsidian.WorldData.Features;

/// <summary>
/// Roots, fungi and sprouts scattered over nylium, like vanilla's NetherForestVegetationFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:nether_forest_vegetation")]
public sealed class NetherForestVegetationFeature : ConfiguredFeatureBase
{
    private static readonly BlockSet nylium = new("#minecraft:nylium");

    public override string Type => "minecraft:nether_forest_vegetation";

    public required IBlockStateProvider StateProvider { get; init; }

    /// <summary>
    /// Horizontal spread; <c>SpreadWidth²</c> attempts are made.
    /// </summary>
    public required int SpreadWidth { get; init; }

    public required int SpreadHeight { get; init; }

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin) || !nylium.Contains(level.GetBlock(origin + Vector.Down)))
            return false;

        if (origin.Y < level.MinY + 1 || origin.Y + 1 > level.MinY + level.Height - 1)
            return false;

        var placed = 0;
        for (var i = 0; i < this.SpreadWidth * this.SpreadWidth; i++)
        {
            var dx = random.NextInt(this.SpreadWidth) - random.NextInt(this.SpreadWidth);
            var dy = random.NextInt(this.SpreadHeight) - random.NextInt(this.SpreadHeight);
            var dz = random.NextInt(this.SpreadWidth) - random.NextInt(this.SpreadWidth);
            var position = origin + new Vector(dx, dy, dz);
            var state = this.StateProvider.GetState(random, position);

            if (level.GetBlock(position).IsAir && position.Y > level.MinY && state.CanSurvive(level, position))
            {
                level.SetBlock(position, state);
                placed++;
            }
        }

        return placed > 0;
    }
}
