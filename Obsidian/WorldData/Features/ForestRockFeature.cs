namespace Obsidian.WorldData.Features;

/// <summary>
/// Mossy boulders made of three overlapping blobs that settle on dirt or stone, like vanilla's BlockBlobFeature
/// (registered as <c>forest_rock</c>).
/// </summary>
[ConfiguredFeatureClass("minecraft:forest_rock")]
public sealed class ForestRockFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:forest_rock";

    public required SimpleBlockState State { get; init; }

    private IBlock Block => field ??= BlocksRegistry.GetFromSimpleState(this.State);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        while (origin.Y > level.MinY + 3)
        {
            var below = level.GetBlock(origin + Vector.Down);
            if (!below.IsAir && (FeatureHelpers.IsDirt(below) || FeatureHelpers.IsStone(below)))
                break;

            origin += Vector.Down;
        }

        if (origin.Y <= level.MinY + 3)
            return false;

        for (var i = 0; i < 3; i++)
        {
            var sizeX = random.NextInt(2);
            var sizeY = random.NextInt(2);
            var sizeZ = random.NextInt(2);
            var radius = (sizeX + sizeY + sizeZ) * 0.333f + 0.5f;

            foreach (var position in FeatureHelpers.BetweenClosed(origin - new Vector(sizeX, sizeY, sizeZ), origin + new Vector(sizeX, sizeY, sizeZ)))
            {
                if (FeatureHelpers.DistSqr(position, origin) <= radius * radius)
                    level.SetBlock(position, this.Block);
            }

            origin += new Vector(-1 + random.NextInt(2), -random.NextInt(2), -1 + random.NextInt(2));
        }

        return true;
    }
}
