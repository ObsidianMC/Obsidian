using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private static Feature FreezeSurface() => (context, _, origin) =>
    {
        var level = context.Level;

        for (var dx = 0; dx < 16; dx++)
        {
            for (var dz = 0; dz < 16; dz++)
            {
                var x = origin.X + dx;
                var z = origin.Z + dz;
                var top = new Vector(x, level.GetHeight(HeightmapType.MotionBlocking, x, z), z);
                var below = top + Vector.Down;
                var biome = CodecRegistry.Biomes.All[context.BiomeAt(top)];

                if (FreezeTopLayerFeature.ShouldFreeze(biome, level, below, false))
                {
                    level.SetBlock(below, BlocksRegistry.Ice);
                }

                if (FreezeTopLayerFeature.ShouldSnow(biome, level, top))
                {
                    level.SetBlock(top, BlocksRegistry.Snow);
                    var ground = level.GetBlock(below);

                    if (ground.HasProperty("snowy"))
                    {
                        level.SetBlock(below, ground.WithProperty("snowy", "true"));
                    }
                }
            }
        }

        return true;
    };
}
