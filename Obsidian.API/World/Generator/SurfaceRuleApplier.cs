using Obsidian.API.World.Generator.SurfaceConditions;
using Obsidian.API.World.Generator.SurfaceRules;

namespace Obsidian.API.World.Generator;

/// <summary>
/// Applies surface rules to determine the actual block type at each position.
/// Surface rules transform the base stone terrain into grass, dirt, sand, etc.
/// </summary>
public class SurfaceRuleApplier
{
    private readonly ISurfaceRule rootRule;
    private readonly Noise.NoiseRouter noiseRouter;
    private readonly int seaLevel;
    private readonly int minY;
    private readonly int maxY;

    public SurfaceRuleApplier(ISurfaceRule rootRule, Noise.NoiseRouter noiseRouter, int seaLevel, int minY, int maxY)
    {
        this.rootRule = rootRule;
        this.noiseRouter = noiseRouter;
        this.seaLevel = seaLevel;
        this.minY = minY;
        this.maxY = maxY;
    }

    /// <summary>
    /// Determines the block type for a given position based on surface rules.
    /// </summary>
    /// <param name="x">World X coordinate</param>
    /// <param name="y">World Y coordinate</param>
    /// <param name="z">World Z coordinate</param>
    /// <param name="depth">Depth below surface (0 = surface, positive = deeper)</param>
    /// <param name="isAir">Whether the block above is air (indicates surface)</param>
    /// <returns>The block state to place, or null to keep existing block</returns>
    public SimpleBlockState? Apply(int x, int y, int z, int depth, bool isAir)
    {
        var context = new SurfaceRuleContext
        {
            X = x,
            Y = y,
            Z = z,
            Depth = depth,
            IsAir = isAir,
            NoiseRouter = noiseRouter,
            SeaLevel = seaLevel,
            MinY = minY,
            MaxY = maxY
        };

        return EvaluateRule(rootRule, context);
    }

    private SimpleBlockState? EvaluateRule(ISurfaceRule rule, SurfaceRuleContext context)
    {
        return rule switch
        {
            BlockSurfaceRule block => block.ResultState,

            SequenceSurfaceRule sequence => EvaluateSequence(sequence, context),

            ConditionSurfaceRule condition => EvaluateCondition(condition, context),

            BandlandsSurfaceRule => null,

            _ => null
        };
    }

    private SimpleBlockState? EvaluateSequence(SequenceSurfaceRule sequence, SurfaceRuleContext context)
    {
        // Try each rule in order until one returns a block
        foreach (var rule in sequence.Sequence)
        {
            var result = EvaluateRule(rule, context);
            if (result != null)
                return result;
        }
        return null;
    }

    private SimpleBlockState? EvaluateCondition(ConditionSurfaceRule condition, SurfaceRuleContext context)
    {
        // Only apply the rule if the condition is true
        if (EvaluateCondition(condition.IfTrue, context))
        {
            return EvaluateRule(condition.ThenRun, context);
        }
        return null;
    }

    private bool EvaluateCondition(ISurfaceCondition condition, SurfaceRuleContext context)
    {
        return condition switch
        {
            // Y-level checks
            YAboveSurfaceCondition yAbove => context.Y >= ResolveVerticalAnchor(yAbove.Anchor, context),

            VerticalGradient gradient => EvaluateVerticalGradient(gradient, context),

            // Surface checks
            AbovePreliminarySurfaceCondition => context.IsAir, // Block above is air

            StoneDepthSurfaceCondition stoneDepth => EvaluateStoneDepth(stoneDepth, context),

            WaterSurfaceCondition water => EvaluateWater(water, context),

            // Terrain checks
            SteepSurfaceCondition => false,

            HoleSurfaceCondition => false,

            // Noise checks
            NoiseThresholdSurfaceCondition noiseThreshold => EvaluateNoiseThreshold(noiseThreshold, context),

            // Biome check
            BiomeSurfaceCondition biome => false,

            // Temperature check
            TemperatureSurfaceCondition => false,

            // Logic operators
            NotSurfaceCondition not => !EvaluateCondition(not.Invert, context),

            _ => false
        };
    }

    private int ResolveVerticalAnchor(VerticalAnchor anchor, SurfaceRuleContext context)
    {
        if (anchor.Absolute.HasValue)
            return anchor.Absolute.Value;

        if (anchor.AboveBottom.HasValue)
            return context.MinY + anchor.AboveBottom.Value;

        if (anchor.BelowTop.HasValue)
            return context.MaxY - anchor.BelowTop.Value;

        return 0;
    }

    private bool EvaluateVerticalGradient(VerticalGradient gradient, SurfaceRuleContext context)
    {
        var minY = ResolveVerticalAnchor(gradient.TrueAtAndBelow, context);
        var maxY = ResolveVerticalAnchor(gradient.FalseAtAndAbove, context);

        if (context.Y <= minY)
            return true;
        if (context.Y >= maxY)
            return false;

        // Linear interpolation between min and max
        double ratio = (double)(context.Y - minY) / (maxY - minY);

        // Use a deterministic hash based on position and random name
        var hash = HashCode.Combine(context.X, context.Y, context.Z, gradient.RandomName);
        var random = new Random(hash);
        return random.NextDouble() > ratio;
    }

    private bool EvaluateStoneDepth(StoneDepthSurfaceCondition stoneDepth, SurfaceRuleContext context)
    {
        // This checks if we're within a certain depth from the surface
        // The "stone depth" is how far below the "preliminary surface" we are

        int effectiveDepth = context.Depth;

        if (stoneDepth.AddSurfaceDepth)
        {
            // Add noise-based variation to depth
            double noise = context.NoiseRouter.FinalDensity?.GetValue(context.X, context.Y, context.Z) ?? 0;
            effectiveDepth += (int)(noise * stoneDepth.SecondaryDepthRange);
        }

        // Check if depth is within offset range
        return effectiveDepth <= stoneDepth.Offset;
    }

    private bool EvaluateWater(WaterSurfaceCondition water, SurfaceRuleContext context)
    {
        // Check if we're at or below sea level and there should be water
        return context.Y <= context.SeaLevel - water.Offset && !context.IsAir;
    }

    private bool EvaluateNoiseThreshold(NoiseThresholdSurfaceCondition noiseThreshold, SurfaceRuleContext context)
    {
        if (noiseThreshold.Noise == null)
            return false;

        double value = noiseThreshold.Noise.GetValue(context.X, 0, context.Z);
        return value >= noiseThreshold.MinThreshold && value <= noiseThreshold.MaxThreshold;
    }
}

/// <summary>
/// Context information passed to surface rule evaluation.
/// </summary>
public struct SurfaceRuleContext
{
    public int X;
    public int Y;
    public int Z;
    public int Depth; // Depth below surface (0 = surface)
    public bool IsAir; // Is the block above air?
    public Noise.NoiseRouter NoiseRouter;
    public int SeaLevel;
    public int MinY; // Minimum Y level for the world
    public int MaxY; // Maximum Y level for the world
}
