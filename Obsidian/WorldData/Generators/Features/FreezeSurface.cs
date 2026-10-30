using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Mojang;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private Feature FreezeSurface() => (context, _, origin) =>
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

                var cold = BiomeTemperature.ColdEnoughToSnow(biome, x, top.Y, z, level.SeaLevel);
                var state = level.GetBlock(top);
                var snow = biome.Element.HasPrecipitation && cold && !level.IsOutsideBuildHeight(top.Y)
                    && state.LightEmission() < 10 && (state.IsAir || state.Is(BlockIds.Snow))
                    && this.SnowCanSurvive(level, top);

                if (snow)
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
