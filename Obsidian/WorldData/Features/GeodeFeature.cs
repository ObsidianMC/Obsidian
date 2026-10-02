using Obsidian.API.Noise;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Providers.IntProviders;
using System.Collections.Concurrent;

namespace Obsidian.WorldData.Features;

/// <summary>
/// Amethyst geodes: nested shells around random distribution points with an optional crack and crystal buds, like vanilla's
/// GeodeFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:geode")]
public sealed class GeodeFeature : ConfiguredFeatureBase
{
    // Vanilla rebuilds this noise from the world seed on every placement; it only depends on the seed, so it is cached.
    private static readonly ConcurrentDictionary<long, NormalNoise> noiseBySeed = new();

    private static IBlock Air => field ??= BlocksRegistry.Get(Material.Air);

    public override string Type => "minecraft:geode";

    public required GeodeBlockSettings Blocks { get; init; }

    public required GeodeLayerSettings Layers { get; init; }

    public required GeodeCrackSettings Crack { get; init; }

    public double UsePotentialPlacementsChance { get; init; } = 0.35;

    public double UseAlternateLayer0Chance { get; init; }

    public bool PlacementsRequireLayer0Alternate { get; init; } = true;

    public IIntProvider OuterWallDistance { get; init; } = new UniformIntProvider { MinInclusive = 4, MaxInclusive = 5 };

    public IIntProvider DistributionPoints { get; init; } = new UniformIntProvider { MinInclusive = 3, MaxInclusive = 4 };

    public IIntProvider PointOffset { get; init; } = new UniformIntProvider { MinInclusive = 1, MaxInclusive = 2 };

    public int MinGenOffset { get; init; } = -16;

    public int MaxGenOffset { get; init; } = 16;

    public double NoiseMultiplier { get; init; } = 0.05;

    /// <summary>
    /// How many distribution points may land in air or invalid blocks before the geode is abandoned.
    /// </summary>
    public required int InvalidBlocksThreshold { get; init; }

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        var pointCount = this.DistributionPoints.Sample(random);
        var noise = noiseBySeed.GetOrAdd(level.Seed, seed => NormalNoise.Create(new WorldgenRandom(new LegacyRandomSource(seed)), -4, [1.0]));
        var pointScale = (double)pointCount / this.OuterWallDistance.MaxValue;

        var filling = 1.0 / Math.Sqrt(this.Layers.Filling);
        var inner = 1.0 / Math.Sqrt(this.Layers.InnerLayer + pointScale);
        var middle = 1.0 / Math.Sqrt(this.Layers.MiddleLayer + pointScale);
        var outer = 1.0 / Math.Sqrt(this.Layers.OuterLayer + pointScale);
        var crackSize = 1.0 / Math.Sqrt(this.Crack.BaseCrackSize + random.NextDouble() / 2.0 + (pointCount > 3 ? pointScale : 0.0));
        var generateCrack = random.NextFloat() < this.Crack.GenerateCrackChance;

        var points = new List<(Vector Position, int Offset)>();
        var invalid = 0;
        for (var i = 0; i < pointCount; i++)
        {
            var x = this.OuterWallDistance.Sample(random);
            var y = this.OuterWallDistance.Sample(random);
            var z = this.OuterWallDistance.Sample(random);
            var point = origin + new Vector(x, y, z);
            var state = level.GetBlock(point);
            if ((state.IsAir || this.Blocks.InvalidBlocks.Contains(state)) && ++invalid > this.InvalidBlocksThreshold)
                return false;

            points.Add((point, this.PointOffset.Sample(random)));
        }

        var cracks = new List<Vector>();
        if (generateCrack)
        {
            var side = random.NextInt(4);
            var distance = pointCount * 2 + 1;
            var crackX = side is 0 or 2 ? distance : 0;
            var crackZ = side is 1 or 2 ? distance : 0;
            cracks.Add(origin + new Vector(crackX, 7, crackZ));
            cracks.Add(origin + new Vector(crackX, 5, crackZ));
            cracks.Add(origin + new Vector(crackX, 1, crackZ));
        }

        var potentialPlacements = new List<Vector>();
        Func<IBlock, bool> canReplace = state => !this.Blocks.CannotReplace.Contains(state);
        var min = new Vector(this.MinGenOffset);
        var max = new Vector(this.MaxGenOffset);

        foreach (var position in FeatureHelpers.BetweenClosed(origin + min, origin + max))
        {
            var noiseValue = noise.GetValue(position.X, position.Y, position.Z) * this.NoiseMultiplier;
            var shell = 0.0;
            var crack = 0.0;

            foreach (var (point, offset) in points)
                shell += 1.0 / Math.Sqrt(FeatureHelpers.DistSqr(position, point) + offset) + noiseValue;

            foreach (var crackPoint in cracks)
                crack += 1.0 / Math.Sqrt(FeatureHelpers.DistSqr(position, crackPoint) + this.Crack.CrackPointOffset) + noiseValue;

            if (shell < outer)
                continue;

            if (generateCrack && crack >= crackSize && shell < filling)
            {
                FeatureHelpers.SafeSetBlock(level, position, Air, canReplace);

                // Fluids next to the crack start flowing into it.
                foreach (var face in FeatureHelpers.Directions)
                {
                    var neighbor = position.Offset(face);
                    if (level.GetBlock(neighbor).HasFluid())
                        level.ScheduleFluidTick(neighbor);
                }
            }
            else if (shell >= filling)
            {
                FeatureHelpers.SafeSetBlock(level, position, this.Blocks.FillingProvider.GetState(random, position), canReplace);
            }
            else if (shell >= inner)
            {
                var alternate = random.NextFloat() < this.UseAlternateLayer0Chance;
                var layer = alternate ? this.Blocks.AlternateInnerLayerProvider : this.Blocks.InnerLayerProvider;
                FeatureHelpers.SafeSetBlock(level, position, layer.GetState(random, position), canReplace);

                if ((!this.PlacementsRequireLayer0Alternate || alternate) && random.NextFloat() < this.UsePotentialPlacementsChance)
                    potentialPlacements.Add(position);
            }
            else if (shell >= middle)
            {
                FeatureHelpers.SafeSetBlock(level, position, this.Blocks.MiddleLayerProvider.GetState(random, position), canReplace);
            }
            else
            {
                FeatureHelpers.SafeSetBlock(level, position, this.Blocks.OuterLayerProvider.GetState(random, position), canReplace);
            }
        }

        var placements = this.Blocks.InnerPlacementBlocks;
        foreach (var position in potentialPlacements)
        {
            var bud = placements[random.NextInt(placements.Length)];

            foreach (var face in FeatureHelpers.Directions)
            {
                if (bud.HasProperty("facing"))
                    bud = bud.WithProperty("facing", FeatureHelpers.FaceName(face));

                var target = position.Offset(face);
                var existing = level.GetBlock(target);
                if (bud.HasProperty("waterlogged"))
                    bud = bud.WithProperty("waterlogged", existing.IsFluidSource());

                // BuddingAmethystBlock.canClusterGrowAtState: air or a full water block.
                if (existing.IsAir || existing.Material == Material.Water && existing.FluidAmount() == 8)
                {
                    FeatureHelpers.SafeSetBlock(level, target, bud, canReplace);
                    break;
                }
            }
        }

        return true;
    }
}

/// <summary>
/// Blocks used by a <see cref="GeodeFeature"/>, like vanilla's GeodeBlockSettings.
/// </summary>
public sealed class GeodeBlockSettings
{
    public required IBlockStateProvider FillingProvider { get; init; }

    public required IBlockStateProvider InnerLayerProvider { get; init; }

    public required IBlockStateProvider AlternateInnerLayerProvider { get; init; }

    public required IBlockStateProvider MiddleLayerProvider { get; init; }

    public required IBlockStateProvider OuterLayerProvider { get; init; }

    /// <summary>
    /// Crystal bud states attached to the inner layer.
    /// </summary>
    public required ImmutableArray<SimpleBlockState> InnerPlacements { get; init; }

    public required BlockSet CannotReplace { get; init; }

    public required BlockSet InvalidBlocks { get; init; }

    internal IBlock[] InnerPlacementBlocks => field ??= [.. this.InnerPlacements.Select(BlocksRegistry.GetFromSimpleState)];
}

/// <summary>
/// Shell thicknesses of a <see cref="GeodeFeature"/>, like vanilla's GeodeLayerSettings.
/// </summary>
public sealed class GeodeLayerSettings
{
    public double Filling { get; init; } = 1.7;

    public double InnerLayer { get; init; } = 2.2;

    public double MiddleLayer { get; init; } = 3.2;

    public double OuterLayer { get; init; } = 4.2;
}

/// <summary>
/// Crack settings of a <see cref="GeodeFeature"/>, like vanilla's GeodeCrackSettings.
/// </summary>
public sealed class GeodeCrackSettings
{
    public double GenerateCrackChance { get; init; } = 1.0;

    public double BaseCrackSize { get; init; } = 2.0;

    public int CrackPointOffset { get; init; } = 2;
}
