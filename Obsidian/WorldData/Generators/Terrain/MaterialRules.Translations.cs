using Obsidian.API.World.Generator.Noise;
using Obsidian.API.World.Generator.SurfaceConditions;
using Obsidian.API.World.Generator.SurfaceRules;
using Obsidian.WorldData.Generators.Density;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using SurfaceAnchor = Obsidian.API.World.Generator.SurfaceConditions.VerticalAnchor;

namespace Obsidian.WorldData.Generators.Terrain;

/// <summary>
/// Vanilla's rule and condition classes of the plugin API (<see cref="BlockSurfaceRule"/>,
/// <see cref="SteepSurfaceCondition"/>, ...) as 26.3 material rules, for plugins' rules that hold or make them: a
/// plugin's rule may keep a <see cref="BlockSurfaceRule"/> to fall back on, or have a property of one, which its
/// context evaluates (see <see cref="ISurfaceContext.Apply"/>).
/// </summary>
/// <remarks>
/// <para>
/// Each object is translated once per generator, by identity, and kept as long as the plugin keeps it: what a block
/// evaluates is the compiled rule. Sequences, conditions and inversions are composed here, so that they can hold
/// plugins' rules and the 26.3 rules a plugin's rule was read with; the other classes are written as their 26.3 JSON
/// and compiled like the settings' rules, so that they behave the same.
/// </para>
/// <para>
/// Translating can happen while chunks generate, on several threads, so the compiling it shares with the settings'
/// rules (noise slots, the secondary noise) is locked. Biome conditions are matched by name rather than numbered
/// like the settings' (see <see cref="MaterialContext.BiomeId"/>), since a block may have numbered its biome before
/// the condition was translated.
/// </para>
/// </remarks>
internal sealed partial class MaterialRules
{
    private readonly ConditionalWeakTable<ISurfaceRule, Func<MaterialContext, IBlock?>> translatedRules = new();
    private readonly ConditionalWeakTable<ISurfaceCondition, Func<MaterialContext, bool>> translatedConditions = new();
    private readonly Lock translating = new();

    /// <summary>A rule a plugin's rule evaluates through its context, as a 26.3 rule.</summary>
    /// <exception cref="NotSupportedException">It's of a class that isn't vanilla's or a plugin's rule type.
    /// </exception>
    private Func<MaterialContext, IBlock?> Translate(ISurfaceRule rule) => rule switch
    {
        CompiledSurfaceRule compiled => compiled.Rule,
        ICustomSurfaceRule custom => context => custom.Apply(context.Surface),
        _ => this.translatedRules.GetValue(rule, this.TranslateVanilla)
    };

    /// <summary>A condition a plugin's rule or condition tests through its context, as a 26.3 condition.</summary>
    /// <exception cref="NotSupportedException">It's of a class that isn't vanilla's or a plugin's condition type.
    /// </exception>
    private Func<MaterialContext, bool> Translate(ISurfaceCondition condition) => condition switch
    {
        CompiledSurfaceCondition compiled => compiled.Condition,
        ICustomSurfaceCondition custom => context => custom.Test(context.Surface),
        _ => this.translatedConditions.GetValue(condition, this.TranslateVanilla)
    };

    private Func<MaterialContext, IBlock?> TranslateVanilla(ISurfaceRule rule)
    {
        switch (rule)
        {
            case SequenceSurfaceRule sequence:
                return Sequence(sequence.Sequence.Select(this.Translate).ToArray());
            case ConditionSurfaceRule condition:
                var test = this.Translate(condition.IfTrue);
                var then = this.Translate(condition.ThenRun);
                return context => test(context) ? then(context) : null;
            default:
                var json = Serialize(RuleJson(rule));

                lock (this.translating)
                    return this.CompileRule(json);
        }
    }

    private Func<MaterialContext, bool> TranslateVanilla(ISurfaceCondition condition)
    {
        switch (condition)
        {
            case NotSurfaceCondition not:
                var inverted = this.Translate(not.Invert);
                return context => !inverted(context);
            case BiomeSurfaceCondition biome:
                var names = biome.BiomeIs.Select(WorldgenData.Qualify).ToFrozenSet();
                return context => names.Contains(context.Biome);
            default:
                var json = Serialize(ConditionJson(condition));

                lock (this.translating)
                    return this.CompileCondition(json);
        }
    }

    // A rule class without rules in it, as its 26.3 JSON.
    private static JsonObject RuleJson(ISurfaceRule rule) => rule switch
    {
        BlockSurfaceRule block => new JsonObject
        {
            ["type"] = "minecraft:block",
            ["result_state"] = BlockStateJson(block.ResultState)
        },
        BandlandsSurfaceRule => new JsonObject { ["type"] = "minecraft:bandlands" },
        _ => throw Unsupported(rule)
    };

    // A condition class without conditions in it, as its 26.3 JSON; 26.3 kept their fields.
    private static JsonObject ConditionJson(ISurfaceCondition condition) => condition switch
    {
        NoiseThresholdSurfaceCondition threshold => new JsonObject
        {
            ["type"] = "minecraft:noise_threshold",
            ["noise"] = NoiseKey(threshold),
            ["min_threshold"] = threshold.MinThreshold,
            ["max_threshold"] = threshold.MaxThreshold
        },
        VerticalGradient gradient => new JsonObject
        {
            ["type"] = "minecraft:vertical_gradient",
            ["random_name"] = gradient.RandomName,
            ["true_at_and_below"] = AnchorJson(gradient.TrueAtAndBelow),
            ["false_at_and_above"] = AnchorJson(gradient.FalseAtAndAbove)
        },
        YAboveSurfaceCondition above => new JsonObject
        {
            ["type"] = "minecraft:y_above",
            ["anchor"] = AnchorJson(above.Anchor),
            ["surface_depth_multiplier"] = above.SurfaceDepthMultiplier,
            ["add_stone_depth"] = above.AddStoneDepth
        },
        WaterSurfaceCondition water => new JsonObject
        {
            ["type"] = "minecraft:water",
            ["offset"] = water.Offset,
            ["surface_depth_multiplier"] = water.SurfaceDepthMultiplier,
            ["add_stone_depth"] = water.AddStoneDepth
        },
        StoneDepthSurfaceCondition depth => new JsonObject
        {
            ["type"] = "minecraft:stone_depth",
            ["offset"] = depth.Offset,
            ["add_surface_depth"] = depth.AddSurfaceDepth,
            ["secondary_depth_range"] = depth.SecondaryDepthRange,
            ["surface_type"] = depth.SurfaceType
        },
        AbovePreliminarySurfaceCondition => new JsonObject { ["type"] = "minecraft:above_preliminary_surface" },
        HoleSurfaceCondition => new JsonObject { ["type"] = "minecraft:hole" },
        SteepSurfaceCondition => new JsonObject { ["type"] = "minecraft:steep" },
        TemperatureSurfaceCondition => new JsonObject { ["type"] = "minecraft:temperature" },
        _ => throw Unsupported(condition)
    };

    private static JsonObject BlockStateJson(SimpleBlockState state)
    {
        var properties = new JsonObject();

        foreach (var (name, value) in state.Properties)
            properties[name] = value;

        return new JsonObject
        {
            ["id"] = state.Name,
            ["properties"] = properties
        };
    }

    // An anchor with none of its heights set is y 0, as vanilla's resolves it.
    private static JsonObject AnchorJson(SurfaceAnchor anchor)
    {
        if (anchor.AboveBottom is int aboveBottom)
            return new JsonObject { ["above_bottom"] = aboveBottom };

        if (anchor.BelowTop is int belowTop)
            return new JsonObject { ["below_top"] = belowTop };

        return new JsonObject { ["absolute"] = anchor.Absolute ?? 0 };
    }

    // The registered noise a condition samples: 26.3 seeds its own noise of that identifier for the world.
    private static string NoiseKey(NoiseThresholdSurfaceCondition threshold) => threshold.Noise switch
    {
        BaseNoise { Key: var key } when key.Length > 0 && !key.StartsWith("obsidian:", StringComparison.Ordinal) => key,
        _ => throw new NotSupportedException(
            $"A minecraft:noise_threshold condition's noise ({threshold.Noise.Type}) isn't a registered noise, which "
            + "the 26.3 material rules sample by identifier.")
    };

    private static JsonElement Serialize(JsonObject json) => JsonSerializer.SerializeToElement(json);

    private static NotSupportedException Unsupported(IRegistryResource value) =>
        new($"{value.Type} ({value.GetType().Name}) isn't one of vanilla's rule or condition classes, nor a plugin's "
            + $"{nameof(ICustomSurfaceRule)} or {nameof(ICustomSurfaceCondition)}, so the 26.3 material rules can't "
            + "evaluate it.");

    // A sequence's rules: the first block one gives.
    private static Func<MaterialContext, IBlock?> Sequence(Func<MaterialContext, IBlock?>[] rules) => context =>
    {
        foreach (var rule in rules)
        {
            var result = rule(context);

            if (result is not null)
            {
                return result;
            }
        }

        return null;
    };
}
