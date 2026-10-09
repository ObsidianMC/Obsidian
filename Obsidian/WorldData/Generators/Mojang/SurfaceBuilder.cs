using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.Noise;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.API.World.Generator.SurfaceConditions;
using Obsidian.API.World.Generator.SurfaceRules;
using System.Threading;
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

    // A build's buffers, kept by each thread for its next build. Taken while in use. They don't reference the builder, so
    // idle threads don't keep it alive.
    private readonly ThreadLocal<SurfaceBuffers?> freeBuffers = new();

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

    /// <param name="noiseChunk">The chunk's noise chunk when the other steps share it, or <c>null</c> for a new one.</param>
    public void BuildSurface(IChunk chunk, NoiseChunk? noiseChunk = null)
    {
        if (this.rule is null)
            return;

        var buffers = this.freeBuffers.Value ?? new SurfaceBuffers(this.biomeSource, this.randomState.Seed, this.minY, this.height);
        this.freeBuffers.Value = null;
        buffers.MoveTo(chunk);

        var context = new SurfaceContext(this, chunk, noiseChunk ?? new NoiseChunk(this.randomState, chunk.X, chunk.Z),
            buffers.BiomeManager, buffers.SurfaceHeights, buffers.ColumnKinds);

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

        buffers.MoveTo(null);
        this.freeBuffers.Value = buffers;
    }

    /// <summary>
    /// Evaluates the surface rule for a single block as if it were the top of the surface, used by carvers
    /// to put grass back on dirt they uncover. Returns <c>null</c> when no rule applies.
    /// </summary>
    public IBlock? TopMaterial(IChunk chunk, NoiseChunk noiseChunk, BiomeManager biomes, int x, int y, int z, bool fluidAbove)
    {
        if (this.rule is null)
            return null;

        var context = new SurfaceContext(this, chunk, noiseChunk, biomes, surfaceHeights: null, columnKinds: null);
        context.UpdateXZ(x & 15, z & 15, x, z);
        context.UpdateY(1, 1, fluidAbove ? y + 1 : int.MinValue, y);

        return this.rule(context);
    }

    private void BuildColumn(SurfaceContext context, int localX, int localZ, int x, int z)
    {
        var stoneDepthAbove = 0;
        var waterHeight = int.MinValue;
        var nextCeilingStoneY = int.MaxValue;

        // The column's blocks are read once (the stone ceiling search reads ahead of the loop) and sorted by reference, as
        // most of them are the same few blocks.
        var surfaceHeight = context.SurfaceHeight(localX, localZ);
        var kinds = context.ReadColumn(localX, localZ, surfaceHeight, this.defaultBlock);

        for (var y = surfaceHeight; y >= this.minY; y--)
        {
            var kind = kinds[y - this.minY];

            if (kind == BlockKind.Air)
            {
                stoneDepthAbove = 0;
                waterHeight = int.MinValue;
                continue;
            }

            if (kind == BlockKind.Liquid)
            {
                if (waterHeight == int.MinValue)
                    waterHeight = y + 1;

                continue;
            }

            if (nextCeilingStoneY >= y)
            {
                nextCeilingStoneY = WayBelowMinY;

                // Below the noise's range reads as air, which isn't stone.
                for (var below = y - 1; below >= this.minY - 1; below--)
                {
                    if (below < this.minY || kinds[below - this.minY] is BlockKind.Air or BlockKind.Liquid)
                    {
                        nextCeilingStoneY = below + 1;
                        break;
                    }
                }
            }

            stoneDepthAbove++;
            context.UpdateY(stoneDepthAbove, y - nextCeilingStoneY + 1, waterHeight, y);

            if (kind == BlockKind.Default && this.rule!(context) is IBlock replacement)
                context.SetBlock(localX, y, localZ, replacement);
        }
    }

    /// <summary>
    /// What <see cref="BuildColumn"/> needs to know about a block.
    /// </summary>
    private enum BlockKind : byte
    {
        Air,
        Liquid,

        /// <summary>
        /// The settings' default block, which the surface rule may replace.
        /// </summary>
        Default,
        Other
    }

    private int GetSurfaceDepth(int x, int z)
    {
        var noise = this.surfaceNoise.GetValue(x, 0.0, z);
        return (int)(noise * 2.75 + 3.0 + new PositionalRandom(this.randomState.Random, x, 0, z).NextDouble() * 0.25);
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

        var random = new PositionalRandom(this.randomState.Random, x, 0, z);
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
                var rules = sequence.Sequence.Select(this.CompileRule).ToArray();
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
                // Matches by registered biome id, which stands for the name, so blocks don't hash a string each.
                var names = biome.BiomeIs.ToHashSet();
                var registered = CodecRegistry.Biomes.All.Values;
                var matches = new bool[registered.Max(codec => codec.Id) + 1];
                foreach (var codec in registered)
                    matches[codec.Id] = names.Contains(codec.Name);

                return context => matches[context.Biome.Id];
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
                    return new PositionalRandom(gradientRandom, context.BlockX, y, context.BlockZ).NextFloat() < chance;
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
        private readonly IBiomeSource fallback;

        public ChunkBiomeSource(IBiomeSource fallback) => this.fallback = fallback;

        public IChunk? Chunk { get; set; }

        public BiomeCodec GetNoiseBiome(int quartX, int quartY, int quartZ) =>
            quartX >> 2 == this.Chunk!.X && quartZ >> 2 == this.Chunk.Z
                ? this.Chunk.GetBiome((quartX & 3) << 2, quartY << 2, (quartZ & 3) << 2)
                : this.fallback.GetNoiseBiome(quartX, quartY, quartZ);
    }

    /// <summary>
    /// What a surface build allocates, kept by each thread for its next build.
    /// </summary>
    private sealed class SurfaceBuffers
    {
        private readonly ChunkBiomeSource biomes;

        public BiomeManager BiomeManager { get; }

        public int[] SurfaceHeights { get; } = new int[256];

        public BlockKind[] ColumnKinds { get; }

        public SurfaceBuffers(IBiomeSource biomeSource, long seed, int minY, int height)
        {
            this.biomes = new ChunkBiomeSource(biomeSource);
            this.BiomeManager = new BiomeManager(this.biomes, seed, minY, height, capacity: 256);
            this.ColumnKinds = new BlockKind[height + 1];
        }

        /// <summary>
        /// Prepares the buffers for building the surface of <paramref name="chunk"/>, or lets go of the last chunk.
        /// </summary>
        public void MoveTo(IChunk? chunk)
        {
            this.biomes.Chunk = chunk;
            if (chunk is not null)
                this.BiomeManager.FocusOn(chunk.X, chunk.Z);
            else
                this.BiomeManager.ClearCache();
        }
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
        // Null when heights are read live from the chunk (one-off evaluations during carving).
        private readonly int[]? surfaceHeights;

        private BlockKind[]? columnKinds;

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

        /// <param name="surfaceHeights">256 heights to track the surface in, or <c>null</c> to read heights live.</param>
        /// <param name="columnKinds">A buffer for <see cref="ReadColumn"/> of a block more than the noise's height, or
        /// <c>null</c> for one on first use.</param>
        public SurfaceContext(SurfaceBuilder builder, IChunk chunk, NoiseChunk noiseChunk, BiomeManager biomeManager, int[]? surfaceHeights,
            BlockKind[]? columnKinds)
        {
            this.builder = builder;
            this.chunk = chunk;
            this.noiseChunk = noiseChunk;
            this.BiomeManager = biomeManager;
            this.columnKinds = columnKinds;

            if (surfaceHeights is not null)
            {
                this.surfaceHeights = surfaceHeights;
                for (var column = 0; column < surfaceHeights.Length; column++)
                    surfaceHeights[column] = this.ScanSurfaceHeight(column % 16, column / 16);
            }
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

        public int SurfaceHeight(int localX, int localZ) =>
            this.surfaceHeights?[localZ * 16 + localX] ?? this.ScanSurfaceHeight(localX, localZ);

        public IBlock GetBlock(int localX, int y, int localZ) =>
            y < this.builder.minY || y >= this.builder.minY + this.builder.height ? BlocksRegistry.Air : this.chunk.GetBlock(localX, y, localZ);

        /// <summary>
        /// The kinds of a column's blocks from the bottom of the noise's range up to <paramref name="top"/>, indexed from the
        /// bottom. Only valid until the next call.
        /// </summary>
        public ReadOnlySpan<BlockKind> ReadColumn(int localX, int localZ, int top, IBlock defaultBlock)
        {
            var minY = this.builder.minY;
            this.columnKinds ??= new BlockKind[this.builder.height + 1];
            IBlock? last = null;
            var lastKind = BlockKind.Air;

            for (var y = minY; y <= top; y++)
            {
                var block = this.GetBlock(localX, y, localZ);
                if (!ReferenceEquals(block, last))
                {
                    last = block;
                    lastKind = block.IsAir ? BlockKind.Air
                        : block.IsLiquid ? BlockKind.Liquid
                        : IsSameBlock(block, defaultBlock) ? BlockKind.Default
                        : BlockKind.Other;
                }

                this.columnKinds[y - minY] = lastKind;
            }

            return this.columnKinds;
        }

        public void SetBlock(int localX, int y, int localZ, IBlock block)
        {
            if (y < this.builder.minY || y >= this.builder.minY + this.builder.height)
                return;

            this.chunk.SetBlock(localX, y, localZ, block);

            var column = localZ * 16 + localX;
            if (this.surfaceHeights is not null && !block.IsAir && y >= this.surfaceHeights[column])
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

        private int ScanSurfaceHeight(int localX, int localZ)
        {
            var y = Math.Min(this.builder.minY + this.builder.height - 1, ChunkColumns.HighestNonAirY(this.chunk));
            while (y >= this.builder.minY && this.chunk.GetBlock(localX, y, localZ).IsAir)
                y--;

            return y + 1;
        }
    }
}
