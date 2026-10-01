namespace Obsidian.WorldData.Features;

/// <summary>
/// A glowstone cluster hanging from the nether ceiling, grown by 1500 random attachment attempts, like vanilla's
/// GlowstoneFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:glowstone_blob")]
public sealed class GlowstoneBlobFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:glowstone_blob";

    private static IBlock Glowstone => field ??= BlocksRegistry.Get(Material.Glowstone);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin) || !level.GetBlock(origin).IsAir)
            return false;

        var ceiling = level.GetBlock(origin + Vector.Up);
        if (ceiling.Material is not (Material.Netherrack or Material.Basalt or Material.Blackstone))
            return false;

        level.SetBlock(origin, Glowstone);

        for (var i = 0; i < 1500; i++)
        {
            var position = origin + new Vector(random.NextInt(8) - random.NextInt(8), -random.NextInt(12), random.NextInt(8) - random.NextInt(8));
            if (!level.GetBlock(position).IsAir)
                continue;

            // Grow only where exactly one neighbor is already glowstone.
            var neighbors = 0;
            foreach (var face in FeatureHelpers.Directions)
            {
                if (level.GetBlock(position.Offset(face)).Material == Material.Glowstone)
                    neighbors++;

                if (neighbors > 1)
                    break;
            }

            if (neighbors == 1)
                level.SetBlock(position, Glowstone);
        }

        return true;
    }
}
