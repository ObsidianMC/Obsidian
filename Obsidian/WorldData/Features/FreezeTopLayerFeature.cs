using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.WorldData.Generators.Mojang;

namespace Obsidian.WorldData.Features;

/// <summary>
/// Freezes surface water and drops snow layers on the 16x16 columns of the chunk, like vanilla's SnowAndFreezeFeature.
/// </summary>
/// <remarks>
/// Placed once per chunk at the chunk origin. Uses the MOTION_BLOCKING height, so snow lands on leaves and ice forms on the top
/// water block.
/// </remarks>
[ConfiguredFeatureClass("minecraft:freeze_top_layer")]
public sealed class FreezeTopLayerFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:freeze_top_layer";

    private static IBlock Ice => field ??= BlocksRegistry.Get(Material.Ice);

    private static IBlock Snow => field ??= BlocksRegistry.Get(Material.Snow);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        for (var dx = 0; dx < 16; dx++)
        {
            for (var dz = 0; dz < 16; dz++)
            {
                var x = origin.X + dx;
                var z = origin.Z + dz;
                var top = new Vector(x, level.GetHeight(HeightmapType.MotionBlocking, x, z), z);
                var below = top + Vector.Down;
                var biome = level.GetBiome(top);

                if (ShouldFreeze(biome, level, below, false))
                    level.SetBlock(below, Ice);

                if (ShouldSnow(biome, level, top))
                {
                    level.SetBlock(top, Snow);

                    var ground = level.GetBlock(below);
                    if (ground.HasProperty("snowy"))
                        level.SetBlock(below, ground.WithProperty("snowy", true));
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Vanilla <c>Biome.shouldFreeze(level, pos, mustBeAtEdge)</c>: cold enough and the position holds a water source block.
    /// </summary>
    /// <remarks>
    /// Block light is the block's own emission (chunks are unlit while features run).
    /// </remarks>
    internal static bool ShouldFreeze(BiomeCodec biome, IWorldGenLevel level, Vector position, bool mustBeAtEdge)
    {
        if (BiomeTemperature.GetTemperature(biome, position.X, position.Y, position.Z, level.SeaLevel) >= 0.15f)
            return false;

        if (level.IsOutsideBuildHeight(position.Y))
            return false;

        var block = level.GetBlock(position);
        if (block.LightEmission() >= 10 || block.GetFluid() != FluidKind.Water || block.BlockClass() != "LiquidBlock")
            return false;

        if (!mustBeAtEdge)
            return true;

        var surrounded = FeatureHelpers.IsWaterFluid(level.GetBlock(position + Vector.West))
            && FeatureHelpers.IsWaterFluid(level.GetBlock(position + Vector.East))
            && FeatureHelpers.IsWaterFluid(level.GetBlock(position + Vector.North))
            && FeatureHelpers.IsWaterFluid(level.GetBlock(position + Vector.South));

        return !surrounded;
    }

    /// <summary>
    /// Vanilla <c>Biome.shouldSnow</c>: the biome snows here and a snow layer could survive in the (air or snow) position.
    /// </summary>
    internal static bool ShouldSnow(BiomeCodec biome, IWorldGenLevel level, Vector position)
    {
        if (!biome.Element.HasPrecipitation || !BiomeTemperature.ColdEnoughToSnow(biome, position.X, position.Y, position.Z, level.SeaLevel))
            return false;

        if (level.IsOutsideBuildHeight(position.Y))
            return false;

        var block = level.GetBlock(position);
        if (block.LightEmission() >= 10)
            return false;

        return (block.IsAir || block.Material == Material.Snow) && Snow.CanSurvive(level, position);
    }
}
