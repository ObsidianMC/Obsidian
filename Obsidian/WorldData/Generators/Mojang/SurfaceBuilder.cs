using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.Noise;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.API.World.Generator.SurfaceConditions;
using Obsidian.API.World.Generator.SurfaceRules;
using VerticalAnchor = Obsidian.API.World.Generator.SurfaceConditions.VerticalAnchor;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Replaces the default block near the surface (grass, sand, terracotta bands, bedrock, deepslate...) using the
/// noise settings' surface rule, and adds eroded badlands pillars and frozen ocean icebergs.
/// </summary>
/// <remarks>
/// Mirrors vanilla's SurfaceSystem and SurfaceRules. The rule tree is compiled once into delegates; per-chunk
/// state lives in <see cref="SurfaceContext"/>, so one instance can build surfaces on several threads.
/// </remarks>
internal sealed class SurfaceBuilder
{
    private const int WayBelowMinY = -32512;

    private readonly RandomState randomState;
    private readonly IBiomeSource biomeSource;
    private readonly IBlock defaultBlock;
    private readonly int seaLevel;
    private readonly int minY;
    private readonly int height;
    private readonly IBlock[] clayBands;
    private readonly BaseNoise clayBandsOffsetNoise;
    private readonly BaseNoise badlandsPillarNoise;
    private readonly BaseNoise badlandsPillarRoofNoise;
    private readonly BaseNoise badlandsSurfaceNoise;
    private readonly BaseNoise icebergPillarNoise;
    private readonly BaseNoise icebergPillarRoofNoise;
    private readonly BaseNoise icebergSurfaceNoise;
    private readonly BaseNoise surfaceNoise;
    private readonly BaseNoise surfaceSecondaryNoise;
    private readonly Func<SurfaceContext, IBlock?>? rule;

    public SurfaceBuilder(RandomState randomState, IBiomeSource biomeSource)
    {
        this.randomState = randomState;
        this.biomeSource = biomeSource;

        var settings = randomState.Settings;
        this.defaultBlock = BlocksRegistry.GetFromSimpleState(settings.DefaultBlock);
        this.seaLevel = settings.SeaLevel;
        this.minY = settings.Noise.MinY;
        this.height = settings.Noise.Height;

        this.clayBandsOffsetNoise = randomState.GetOrCreateNoise("minecraft:clay_bands_offset");
        this.clayBands = GenerateBands(randomState.Random.FromHashOf("minecraft:clay_bands"));
        this.surfaceNoise = randomState.GetOrCreateNoise("minecraft:surface");
        this.surfaceSecondaryNoise = randomState.GetOrCreateNoise("minecraft:surface_secondary");
        this.badlandsPillarNoise = randomState.GetOrCreateNoise("minecraft:badlands_pillar");
        this.badlandsPillarRoofNoise = randomState.GetOrCreateNoise("minecraft:badlands_pillar_roof");
        this.badlandsSurfaceNoise = randomState.GetOrCreateNoise("minecraft:badlands_surface");
        this.icebergPillarNoise = randomState.GetOrCreateNoise("minecraft:iceberg_pillar");
        this.icebergPillarRoofNoise = randomState.GetOrCreateNoise("minecraft:iceberg_pillar_roof");
        this.icebergSurfaceNoise = randomState.GetOrCreateNoise("minecraft:iceberg_surface");

        this.rule = settings.SurfaceRule is null ? null : this.CompileRule(settings.SurfaceRule);
    }

    public void BuildSurface(IChunk chunk)
    {
        if (this.rule is null)
            return;

        var context = new SurfaceContext(this, chunk, new NoiseChunk(this.randomState, chunk.X, chunk.Z),
            new BiomeManager(new ChunkBiomeSource(chunk, this.biomeSource), this.randomState.Seed, this.minY, this.height));

        var chunkMinX = chunk.X << 4;
        var chunkMinZ = chunk.Z << 4;

        for (var localX = 0; localX < 16; localX++)
        {
            for (var localZ = 0; localZ < 16; localZ++)
            {
                var x = chunkMinX + localX;
                var z = chunkMinZ + localZ;
                var startHeight = context.SurfaceHeight(localX, localZ);
                var biome = context.BiomeManager.GetBiome(x, this.randomState.Settings.LegacyRandomSource ? 0 : startHeight, z);

                if (biome.Name == "minecraft:eroded_badlands")
                    this.ErodedBadlandsExtension(context, localX, localZ, x, z, startHeight);

                context.UpdateXZ(localX, localZ, x, z);
                this.BuildColumn(context, localX, localZ, x, z);

                if (biome.Name is "minecraft:frozen_ocean" or "minecraft:deep_frozen_ocean")
                    this.FrozenOceanExtension(context, biome, localX, localZ, x, z, startHeight);
            }
        }
    }

    private void BuildColumn(SurfaceContext context, int localX, int localZ, int x, int z)
    {
        var stoneDepthAbove = 0;
        var waterHeight = int.MinValue;
        var nextCeilingStoneY = int.MaxValue;

        for (var y = context.SurfaceHeight(localX, localZ); y >= this.minY; y--)
        {
            var block = context.GetBlock(localX, y, localZ);

            if (block.IsAir)
            {
                stoneDepthAbove = 0;
                waterHeight = int.MinValue;
                continue;
            }

            if (block.IsLiquid)
            {
                if (waterHeight == int.MinValue)
                    waterHeight = y + 1;

                continue;
            }

            if (nextCeilingStoneY >= y)
            {
                nextCeilingStoneY = WayBelowMinY;

                for (var below = y - 1; below >= this.minY - 1; below--)
                {
                    if (!IsStone(context.GetBlock(localX, below, localZ)))
                    {
                        nextCeilingStoneY = below + 1;
                        break;
                    }
                }
            }

            stoneDepthAbove++;
            context.UpdateY(stoneDepthAbove, y - nextCeilingStoneY + 1, waterHeight, y);

            if (IsSameBlock(block, this.defaultBlock) && this.rule!(context) is IBlock replacement)
                context.SetBlock(localX, y, localZ, replacement);
        }
    }

    private int GetSurfaceDepth(int x, int z)
    {
        var noise = this.surfaceNoise.GetValue(x, 0.0, z);
        return (int)(noise * 2.75 + 3.0 + this.randomState.Random.At(x, 0, z).NextDouble() * 0.25);
    }

    private double GetSurfaceSecondary(int x, int z) => this.surfaceSecondaryNoise.GetValue(x, 0.0, z);

    private IBlock GetBand(int x, int y, int z)
    {
        // Java's Math.round, not banker's rounding.
        var offset = (int)Math.Floor(this.clayBandsOffsetNoise.GetValue(x, 0.0, z) * 4.0 + 0.5);
        return this.clayBands[(y + offset + this.clayBands.Length) % this.clayBands.Length];
    }

    private void ErodedBadlandsExtension(SurfaceContext context, int localX, int localZ, int x, int z, int startHeight)
    {
        var pillar = Math.Min(Math.Abs(this.badlandsSurfaceNoise.GetValue(x, 0.0, z) * 8.25),
            this.badlandsPillarNoise.GetValue(x * 0.2, 0.0, z * 0.2) * 15.0);

        if (pillar <= 0.0)
            return;

        var roof = Math.Abs(this.badlandsPillarRoofNoise.GetValue(x * 0.75, 0.0, z * 0.75) * 1.5);
        var top = (int)Math.Floor(64.0 + Math.Min(pillar * pillar * 2.5, Math.Ceiling(roof * 50.0) + 24.0));

        if (startHeight > top)
            return;

        for (var y = top; y >= this.minY; y--)
        {
            var block = context.GetBlock(localX, y, localZ);

            if (block.Material == this.defaultBlock.Material)
                break;

            if (block.Material == Material.Water)
                return;
        }

        for (var y = top; y >= this.minY && context.GetBlock(localX, y, localZ).IsAir; y--)
            context.SetBlock(localX, y, localZ, this.defaultBlock);
    }

    private void FrozenOceanExtension(SurfaceContext context, BiomeCodec biome, int localX, int localZ, int x, int z, int startHeight)
    {
        var minSurfaceLevel = context.MinSurfaceLevel;
        var iceberg = Math.Min(Math.Abs(this.icebergSurfaceNoise.GetValue(x, 0.0, z) * 8.25),
            this.icebergPillarNoise.GetValue(x * 1.28, 0.0, z * 1.28) * 15.0);

        if (iceberg <= 1.8)
            return;

        var roof = Math.Abs(this.icebergPillarRoofNoise.GetValue(x * 1.17, 0.0, z * 1.17) * 1.5);
        var top = Math.Min(iceberg * iceberg * 1.2, Math.Ceiling(roof * 40.0) + 14.0);

        if (BiomeTemperature.ShouldMeltFrozenOceanIcebergSlightly(biome, x, this.seaLevel, z, this.seaLevel))
            top -= 2.0;

        double bottom;
        if (top > 2.0)
        {
            bottom = this.seaLevel - top - 7.0;
            top += this.seaLevel;
        }
        else
        {
            top = 0.0;
            bottom = 0.0;
        }

        var random = this.randomState.Random.At(x, 0, z);
        var maxSnowDepth = 2 + random.NextInt(4);
        var minSnowY = this.seaLevel + 18 + random.NextInt(10);
        var snowDepth = 0;

        for (var y = Math.Max(startHeight, (int)top + 1); y >= minSurfaceLevel; y--)
        {
            var block = context.GetBlock(localX, y, localZ);
            var place = (block.IsAir && y < (int)top && random.NextDouble() > 0.01)
                || (block.Material == Material.Water && y > (int)bottom && y < this.seaLevel && bottom != 0.0 && random.NextDouble() > 0.15);

            if (!place)
                continue;

            if (snowDepth <= maxSnowDepth && y > minSnowY)
            {
                context.SetBlock(localX, y, localZ, BlocksRegistry.SnowBlock);
                snowDepth++;
            }
            else
            {
                context.SetBlock(localX, y, localZ, BlocksRegistry.PackedIce);
            }
        }
    }

    private static IBlock[] GenerateBands(IRandomSource random)
    {
        var bands = new IBlock[192];
        Array.Fill(bands, BlocksRegistry.Terracotta);

        for (var i = 0; i < bands.Length; i++)
        {
            i += random.NextInt(5) + 1;
            if (i < bands.Length)
                bands[i] = BlocksRegistry.OrangeTerracotta;
        }

        MakeBands(random, bands, 1, BlocksRegistry.YellowTerracotta);
        MakeBands(random, bands, 2, BlocksRegistry.BrownTerracotta);
        MakeBands(random, bands, 1, BlocksRegistry.RedTerracotta);

        var whiteBandCount = random.NextIntBetweenInclusive(9, 15);
        var placed = 0;

        for (var i = 0; placed < whiteBandCount && i < bands.Length; i += random.NextInt(16) + 4)
        {
            bands[i] = BlocksRegistry.WhiteTerracotta;

            if (i - 1 > 0 && random.NextBoolean())
                bands[i - 1] = BlocksRegistry.LightGrayTerracotta;

            if (i + 1 < bands.Length && random.NextBoolean())
                bands[i + 1] = BlocksRegistry.LightGrayTerracotta;

            placed++;
        }

        return bands;
    }

    private static void MakeBands(IRandomSource random, IBlock[] bands, int minWidth, IBlock block)
    {
        var count = random.NextIntBetweenInclusive(6, 15);

        for (var i = 0; i < count; i++)
        {
            var width = minWidth + random.NextInt(3);
            var start = random.NextInt(bands.Length);

            for (var offset = 0; start + offset < bands.Length && offset < width; offset++)
                bands[start + offset] = block;
        }
    }

    private static bool IsStone(IBlock block) => !block.IsAir && !block.IsLiquid;

    // Block instances aren't canonical (they're cached per state id and per name), so compare by value.
    private static bool IsSameBlock(IBlock a, IBlock b) =>
        a.Material == b.Material && (a.State is null || b.State is null || a.State.Id == b.State.Id);

    private int ResolveY(VerticalAnchor anchor)
    {
        if (anchor.Absolute is int absolute)
            return absolute;

        if (anchor.AboveBottom is int aboveBottom)
            return this.minY + aboveBottom;

        if (anchor.BelowTop is int belowTop)
            return this.height - 1 + this.minY - belowTop;

        return 0;
    }

    private Func<SurfaceContext, IBlock?> CompileRule(ISurfaceRule rule)
    {
        switch (rule)
        {
            case BlockSurfaceRule block:
                var result = BlocksRegistry.GetFromSimpleState(block.ResultState);
                return _ => result;
            case SequenceSurfaceRule sequence:
                var rules = Array.ConvertAll(sequence.Sequence, this.CompileRule);
                return context =>
                {
                    foreach (var candidate in rules)
                    {
                        if (candidate(context) is IBlock block)
                            return block;
                    }

                    return null;
                };
            case ConditionSurfaceRule condition:
                var test = this.CompileCondition(condition.IfTrue);
                var then = this.CompileRule(condition.ThenRun);
                return context => test(context) ? then(context) : null;
            case BandlandsSurfaceRule:
                return context => this.GetBand(context.BlockX, context.BlockY, context.BlockZ);
            default:
                throw new NotSupportedException($"Unsupported surface rule '{rule.Type}'.");
        }
    }

    private Func<SurfaceContext, bool> CompileCondition(ISurfaceCondition condition)
    {
        switch (condition)
        {
            case BiomeSurfaceCondition biome:
                var biomes = biome.BiomeIs.ToHashSet();
                return context => biomes.Contains(context.Biome.Name);
            case NoiseThresholdSurfaceCondition noiseThreshold:
                var noise = this.randomState.GetOrCreateNoise(((BaseNoise)noiseThreshold.Noise).Key);
                return context =>
                {
                    var value = noise.GetValue(context.BlockX, 0.0, context.BlockZ);
                    return value >= noiseThreshold.MinThreshold && value <= noiseThreshold.MaxThreshold;
                };
            case VerticalGradient gradient:
                var trueAtAndBelow = this.ResolveY(gradient.TrueAtAndBelow);
                var falseAtAndAbove = this.ResolveY(gradient.FalseAtAndAbove);
                var randomName = gradient.RandomName.Contains(':') ? gradient.RandomName : $"minecraft:{gradient.RandomName}";
                var gradientRandom = this.randomState.GetOrCreateRandomFactory(randomName);
                return context =>
                {
                    var y = context.BlockY;
                    if (y <= trueAtAndBelow)
                        return true;

                    if (y >= falseAtAndAbove)
                        return false;

                    var chance = 1.0 + (double)(y - trueAtAndBelow) / (falseAtAndAbove - trueAtAndBelow) * (0.0 - 1.0);
                    return gradientRandom.At(context.BlockX, y, context.BlockZ).NextFloat() < chance;
                };
            case YAboveSurfaceCondition yAbove:
                var anchorY = this.ResolveY(yAbove.Anchor);
                return context => context.BlockY + (yAbove.AddStoneDepth ? context.StoneDepthAbove : 0)
                    >= anchorY + context.SurfaceDepth * yAbove.SurfaceDepthMultiplier;
            case WaterSurfaceCondition water:
                return context => context.WaterHeight == int.MinValue
                    || context.BlockY + (water.AddStoneDepth ? context.StoneDepthAbove : 0)
                        >= context.WaterHeight + water.Offset + context.SurfaceDepth * water.SurfaceDepthMultiplier;
            case StoneDepthSurfaceCondition stoneDepth:
                var ceiling = stoneDepth.SurfaceType == "ceiling";
                return context =>
                {
                    var depth = ceiling ? context.StoneDepthBelow : context.StoneDepthAbove;
                    var surfaceDepth = stoneDepth.AddSurfaceDepth ? context.SurfaceDepth : 0;
                    var secondaryDepth = stoneDepth.SecondaryDepthRange == 0
                        ? 0
                        : (int)((context.SurfaceSecondary + 1.0) / 2.0 * stoneDepth.SecondaryDepthRange);

                    return depth <= 1 + stoneDepth.Offset + surfaceDepth + secondaryDepth;
                };
            case NotSurfaceCondition not:
                var inner = this.CompileCondition(not.Invert);
                return context => !inner(context);
            case AbovePreliminarySurfaceCondition:
                return context => context.BlockY >= context.MinSurfaceLevel;
            case HoleSurfaceCondition:
                return context => context.SurfaceDepth <= 0;
            case SteepSurfaceCondition:
                return context => context.Steep;
            case TemperatureSurfaceCondition:
                return context => BiomeTemperature.ColdEnoughToSnow(context.Biome, context.BlockX, context.BlockY, context.BlockZ, this.seaLevel);
            default:
                throw new NotSupportedException($"Unsupported surface condition '{condition.Type}'.");
        }
    }

    /// <summary>
    /// Reads the chunk's stored biomes and samples neighbors, like vanilla's world gen region does.
    /// </summary>
    private sealed class ChunkBiomeSource : IBiomeSource
    {
        private readonly IChunk chunk;
        private readonly IBiomeSource fallback;

        public ChunkBiomeSource(IChunk chunk, IBiomeSource fallback)
        {
            this.chunk = chunk;
            this.fallback = fallback;
        }

        public BiomeCodec GetNoiseBiome(int quartX, int quartY, int quartZ) =>
            quartX >> 2 == this.chunk.X && quartZ >> 2 == this.chunk.Z
                ? this.chunk.GetBiome((quartX & 3) << 2, quartY << 2, (quartZ & 3) << 2)
                : this.fallback.GetNoiseBiome(quartX, quartY, quartZ);
    }

    /// <summary>
    /// Per-chunk surface state: the current column and block, plus values vanilla caches per column or per block.
    /// </summary>
    private sealed class SurfaceContext
    {
        private readonly SurfaceBuilder builder;
        private readonly IChunk chunk;
        private readonly NoiseChunk noiseChunk;

        // WORLD_SURFACE_WG as "first free Y" per column; kept up to date as surface blocks are placed, like vanilla.
        private readonly int[] surfaceHeights;

        private int localX;
        private int localZ;
        private BiomeCodec? biome;
        private double? surfaceSecondary;
        private int? minSurfaceLevel;
        private bool? steep;

        public BiomeManager BiomeManager { get; }

        public int BlockX { get; private set; }
        public int BlockY { get; private set; }
        public int BlockZ { get; private set; }
        public int SurfaceDepth { get; private set; }
        public int WaterHeight { get; private set; }
        public int StoneDepthAbove { get; private set; }
        public int StoneDepthBelow { get; private set; }

        public SurfaceContext(SurfaceBuilder builder, IChunk chunk, NoiseChunk noiseChunk, BiomeManager biomeManager)
        {
            this.builder = builder;
            this.chunk = chunk;
            this.noiseChunk = noiseChunk;
            this.BiomeManager = biomeManager;
            this.surfaceHeights = CreateSurfaceHeights(chunk, builder.minY, builder.height);
        }

        public BiomeCodec Biome => this.biome ??= this.BiomeManager.GetBiome(this.BlockX, this.BlockY, this.BlockZ);

        public double SurfaceSecondary => this.surfaceSecondary ??= this.builder.GetSurfaceSecondary(this.BlockX, this.BlockZ);

        /// <summary>
        /// Lowest Y that counts as "above the preliminary surface", interpolated from 16-block cell corners.
        /// </summary>
        public int MinSurfaceLevel => this.minSurfaceLevel ??= this.ComputeMinSurfaceLevel();

        /// <summary>
        /// Whether the terrain rises by 4+ blocks towards the north or east within this chunk.
        /// </summary>
        public bool Steep => this.steep ??= this.ComputeSteep();

        public void UpdateXZ(int localX, int localZ, int x, int z)
        {
            this.localX = localX;
            this.localZ = localZ;
            this.BlockX = x;
            this.BlockZ = z;
            this.SurfaceDepth = this.builder.GetSurfaceDepth(x, z);
            this.surfaceSecondary = null;
            this.minSurfaceLevel = null;
            this.steep = null;
        }

        public void UpdateY(int stoneDepthAbove, int stoneDepthBelow, int waterHeight, int y)
        {
            this.BlockY = y;
            this.WaterHeight = waterHeight;
            this.StoneDepthBelow = stoneDepthBelow;
            this.StoneDepthAbove = stoneDepthAbove;
            this.biome = null;
        }

        public int SurfaceHeight(int localX, int localZ) => this.surfaceHeights[localZ * 16 + localX];

        public IBlock GetBlock(int localX, int y, int localZ) =>
            y < this.builder.minY || y >= this.builder.minY + this.builder.height ? BlocksRegistry.Air : this.chunk.GetBlock(localX, y, localZ);

        public void SetBlock(int localX, int y, int localZ, IBlock block)
        {
            if (y < this.builder.minY || y >= this.builder.minY + this.builder.height)
                return;

            this.chunk.SetBlock(localX, y, localZ, block);

            var column = localZ * 16 + localX;
            if (!block.IsAir && y >= this.surfaceHeights[column])
                this.surfaceHeights[column] = y + 1;
        }

        private int ComputeMinSurfaceLevel()
        {
            var cellX = this.BlockX >> 4;
            var cellZ = this.BlockZ >> 4;

            var level00 = this.noiseChunk.PreliminarySurfaceLevel(cellX << 4, cellZ << 4);
            var level10 = this.noiseChunk.PreliminarySurfaceLevel((cellX + 1) << 4, cellZ << 4);
            var level01 = this.noiseChunk.PreliminarySurfaceLevel(cellX << 4, (cellZ + 1) << 4);
            var level11 = this.noiseChunk.PreliminarySurfaceLevel((cellX + 1) << 4, (cellZ + 1) << 4);

            double deltaX = (this.BlockX & 15) / 16.0f;
            double deltaZ = (this.BlockZ & 15) / 16.0f;
            var bottom = level00 + deltaX * (level10 - level00);
            var top = level01 + deltaX * (level11 - level01);

            return (int)Math.Floor(bottom + deltaZ * (top - bottom)) + this.SurfaceDepth - 8;
        }

        private bool ComputeSteep()
        {
            var north = this.SurfaceHeight(this.localX, Math.Max(this.localZ - 1, 0));
            var south = this.SurfaceHeight(this.localX, Math.Min(this.localZ + 1, 15));
            if (south >= north + 4)
                return true;

            var west = this.SurfaceHeight(Math.Max(this.localX - 1, 0), this.localZ);
            var east = this.SurfaceHeight(Math.Min(this.localX + 1, 15), this.localZ);
            return west >= east + 4;
        }

        private static int[] CreateSurfaceHeights(IChunk chunk, int minY, int height)
        {
            var heights = new int[256];

            for (var localZ = 0; localZ < 16; localZ++)
            {
                for (var localX = 0; localX < 16; localX++)
                {
                    var y = minY + height - 1;
                    while (y >= minY && chunk.GetBlock(localX, y, localZ).IsAir)
                        y--;

                    heights[localZ * 16 + localX] = y + 1;
                }
            }

            return heights;
        }
    }
}
