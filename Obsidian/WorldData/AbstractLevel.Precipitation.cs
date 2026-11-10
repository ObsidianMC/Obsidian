using Obsidian.API.Blocks;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Generators.Mojang;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    // Vanilla's Biome.shouldFreeze and shouldSnow: water freezes and snow settles only where the block light is below
    // this.
    private const int PrecipitationLightLimit = 10;

    // Vanilla's CauldronBlock.RAIN_FILL_CHANCE and POWDER_SNOW_FILL_CHANCE.
    private const float RainFillChance = 0.05f;
    private const float SnowFillChance = 0.1f;

    private enum Precipitation
    {
        None,
        Rain,
        Snow
    }

    // The sea level the snow line is measured from (vanilla's Level.getSeaLevel, from the level's generator).
    private int ClimateSeaLevel => this.Generator switch
    {
        MojangGenerator generator => generator.SeaLevel,

        // FlatStemGenerator derives from SuperflatGenerator; vanilla's flat generator reports -63.
        SuperflatGenerator => -63,

        // Generators without a sea-level contract retain the conventional overworld reference height.
        _ => 63
    };

    // Vanilla's getBlockRandomPos: the next offset in a 16-block cube, x, y and z, from the linear congruential
    // sequence random ticks and the precipitation pass share. It has to match vanilla's, because the blocks it picks
    // decide what grows, melts and freezes.
    private (int X, int Y, int Z) NextRandomTickOffset()
    {
        this.randomTickValue = this.randomTickValue * 3 + 1013904223;
        var bits = this.randomTickValue >> 2;

        return (bits & 15, bits >> 16 & 15, bits >> 8 & 15);
    }

    /// <summary>
    /// Vanilla's <c>ServerLevel.tickPrecipitation</c> at the column of <paramref name="chunk"/> the position sequence
    /// picks next: the water at the top of the column freezes where it's cold and dark enough, and while it rains,
    /// snow settles or builds up on it and a cauldron there may fill.
    /// </summary>
    /// <remarks>
    /// Biomes use the same seed-dependent lookup as natural spawning, including neighbouring chunks. The level's
    /// writes notify neighbours, so water beside new ice schedules its flow. Growing snow doesn't push entities up yet.
    /// </remarks>
    private void TickPrecipitation(Chunk chunk)
    {
        var (x, _, z) = this.NextRandomTickOffset();
        var top = new Vector(
            chunk.X * 16 + x,
            chunk.GetHeight(HeightmapType.MotionBlocking, x, z),
            chunk.Z * 16 + z);
        var below = top + Vector.Down;
        var blocks = this.BlockUpdates;

        // Hand-built chunks and plugin-generated chunks may have no biome data, so no climate can be read there.
        if (this.BiomeAt(chunk, top.X, top.Y, top.Z) is not BiomeCodec biome)
            return;

        if (this.ShouldFreeze(chunk, biome, below))
            blocks.SetBlock(below, BlocksRegistry.Ice);

        if (!this.IsRaining())
            return;

        var maxSnow = this.LevelData.GetIntegerRule("max_snow_accumulation_height", 1);
        if (maxSnow > 0 && this.ShouldSnow(chunk, biome, top))
            SettleSnow(blocks, top, maxSnow);

        var precipitation = this.PrecipitationAt(biome, below);
        if (precipitation != Precipitation.None)
            this.FillCauldron(below, blocks.GetBlock(below), precipitation);
    }

    // Vanilla's Biome.shouldFreeze with its neighbour check: cold enough, dark enough, a water source block, and not
    // water on all four sides.
    private bool ShouldFreeze(Chunk chunk, BiomeCodec biome, Vector position)
    {
        if (!this.ColdEnoughToSnow(biome, position))
            return false;

        if (this.IsOutsideBuildHeight(position.Y) || BlockLightIn(chunk, position) >= PrecipitationLightLimit)
            return false;

        var blocks = this.BlockUpdates;
        var block = blocks.GetBlock(position);
        if (block.GetFluid() != FluidKind.Water || block.BlockClass() != "LiquidBlock")
            return false;

        var surrounded = FeatureHelpers.IsWaterFluid(blocks.GetBlock(position + Vector.West))
            && FeatureHelpers.IsWaterFluid(blocks.GetBlock(position + Vector.East))
            && FeatureHelpers.IsWaterFluid(blocks.GetBlock(position + Vector.North))
            && FeatureHelpers.IsWaterFluid(blocks.GetBlock(position + Vector.South));

        return !surrounded;
    }

    // Vanilla's Biome.shouldSnow: snow falls here, it's dark enough, and the air or snow there could hold a layer.
    private bool ShouldSnow(Chunk chunk, BiomeCodec biome, Vector position)
    {
        if (this.PrecipitationAt(biome, position) != Precipitation.Snow)
            return false;

        if (this.IsOutsideBuildHeight(position.Y) || BlockLightIn(chunk, position) >= PrecipitationLightLimit)
            return false;

        var blocks = this.BlockUpdates;
        var state = blocks.GetBlock(position);
        if (!state.IsAir && !state.Is(BlockIds.Snow))
            return false;

        return PlacementSurvival.CanSurvive(blocks, BlocksRegistry.Snow, position);
    }

    // A first layer of snow, or one more on the snow there, up to the game rule's height and a full block.
    private static void SettleSnow(LevelBlockUpdates blocks, Vector position, int maxSnow)
    {
        var state = blocks.GetBlock(position);
        if (!state.Is(BlockIds.Snow))
        {
            blocks.SetBlock(position, BlocksRegistry.Snow);
            return;
        }

        var layers = int.Parse(state.GetProperty("layers")!);
        if (layers < Math.Min(maxSnow, 8))
            blocks.SetBlock(position, state.WithProperty("layers", layers + 1));
    }

    // Vanilla's Biome.getPrecipitationAt: none in a biome without it, snow where it's cold, rain elsewhere.
    private Precipitation PrecipitationAt(BiomeCodec biome, Vector position)
    {
        if (!biome.Element.HasPrecipitation)
            return Precipitation.None;

        return this.ColdEnoughToSnow(biome, position) ? Precipitation.Snow : Precipitation.Rain;
    }

    private bool ColdEnoughToSnow(BiomeCodec biome, Vector position) =>
        BiomeTemperature.ColdEnoughToSnow(biome, position.X, position.Y, position.Z, this.ClimateSeaLevel);

    /// <summary>
    /// Vanilla's <c>handlePrecipitation</c> of the only blocks that override it: an empty cauldron fills with water in
    /// rain and powder snow in snow, and a water or powder snow cauldron gains a level of its own kind.
    /// </summary>
    /// <remarks>
    /// The chance is drawn from the level random before anything else is checked, so a full cauldron or one of the
    /// other kind still draws, and a lava cauldron doesn't: that order decides the draws that follow, so it matches
    /// vanilla's.
    /// </remarks>
    private void FillCauldron(Vector position, IBlock state, Precipitation precipitation)
    {
        var id = state.RegistryId;
        if (id is not (BlockIds.Cauldron or BlockIds.WaterCauldron or BlockIds.PowderSnowCauldron))
            return;

        var chance = precipitation == Precipitation.Rain ? RainFillChance : SnowFillChance;
        if (this.GrowthRandom.NextFloat() >= chance)
            return;

        var center = (VectorD)position + 0.5;
        if (id == BlockIds.Cauldron)
        {
            var filled = precipitation == Precipitation.Rain
                ? BlocksRegistry.WaterCauldron
                : BlocksRegistry.PowderSnowCauldron;
            this.BlockUpdates.SetBlock(position, filled);
            this.EmitGameEvent(MobGameEvent.BlockChange, center);
            return;
        }

        var own = id == BlockIds.WaterCauldron ? Precipitation.Rain : Precipitation.Snow;
        var level = int.Parse(state.GetProperty("level")!);
        if (level == 3 || own != precipitation)
            return;

        var raised = state.WithProperty("level", level + 1);
        this.BlockUpdates.SetBlock(position, raised);
        this.EmitGameEvent(MobGameEvent.BlockChange, center, affectedBlock: raised);
    }

    private static int BlockLightIn(Chunk chunk, Vector position) =>
        chunk.GetLightLevel(position.X, position.Y, position.Z, LightType.Block);
}
