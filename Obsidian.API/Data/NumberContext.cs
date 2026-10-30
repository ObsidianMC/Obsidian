using Obsidian.API.World.Generator.RandomSources;
using System.Text.Json;

namespace Obsidian.API.Data;

/// <summary>
/// Inputs shared by context number providers in loot, recipes and other data-driven systems.
/// Create one context per evaluation; providers themselves are immutable and reusable.
/// </summary>
public sealed class NumberContext
{
    public required IRandomSource Random { get; init; }

    public Func<string, ContextIntProvider?> ResolveInt { get; init; } =
        id => ContextNumberProviders.Ints.GetValueOrDefault(id);

    public Func<string, ContextFloatProvider?> ResolveFloat { get; init; } =
        id => ContextNumberProviders.Floats.GetValueOrDefault(id);

    /// <summary>Evaluates an inline predicate or predicate id in the caller's domain.</summary>
    public Func<JsonElement, bool>? TestCondition { get; init; }

    // World-dependent sources are supplied by the caller, without coupling this component to a server or loot.
    public Func<string, int?>? IntEnvironmentAttribute { get; init; }

    public Func<string, float?>? FloatEnvironmentAttribute { get; init; }

    public Func<string, string, int?>? IntStorage { get; init; }

    public Func<string, string, float?>? FloatStorage { get; init; }

    public Func<JsonElement, string, int?>? Score { get; init; }

    public int? EnchantmentLevel { get; init; }

    private int referenceDepth;

    internal T Resolve<T>(Func<T> evaluate)
    {
        if (this.referenceDepth >= 256)
            throw new System.IO.InvalidDataException("Cyclic or excessively deep number provider reference.");

        this.referenceDepth++;
        try
        {
            return evaluate();
        }
        finally
        {
            this.referenceDepth--;
        }
    }
}

/// <summary>
/// An integer context expression. Arithmetic failures return zero at the evaluation boundary, as in 26.3.
/// </summary>
public sealed class ContextIntProvider
{
    private readonly Func<NumberContext, int> evaluate;

    internal ContextIntProvider(Func<NumberContext, int> evaluate) => this.evaluate = evaluate;

    internal int Evaluate(NumberContext context) => this.evaluate(context);

    public int GetInt(NumberContext context)
    {
        try
        {
            return this.Evaluate(context);
        }
        catch (ArithmeticException)
        {
            return 0;
        }
    }
}

/// <summary>
/// A single-precision context expression. Arithmetic failures and non-finite final values return zero.
/// </summary>
public sealed class ContextFloatProvider
{
    private readonly Func<NumberContext, float> evaluate;

    internal ContextFloatProvider(Func<NumberContext, float> evaluate) => this.evaluate = evaluate;

    internal float Evaluate(NumberContext context) => this.evaluate(context);

    public float GetFloat(NumberContext context)
    {
        try
        {
            var value = this.Evaluate(context);
            return float.IsFinite(value) ? value : 0;
        }
        catch (ArithmeticException)
        {
            return 0;
        }
    }
}
