using Obsidian.API.World.Generator.RandomSources;
using System.Text.Json;

namespace Obsidian.API.Data;

/// <summary>
/// Inputs shared by context number providers in loot, recipes and other data-driven systems.
/// Create one context per evaluation; providers themselves are immutable and reusable.
/// </summary>
public sealed class NumberContext
{
    /// <summary>
    /// Gets the random source random providers draw from.
    /// </summary>
    public required IRandomSource Random { get; init; }

    /// <summary>
    /// Gets or sets how a provider id referenced by another provider resolves to an integer provider; vanilla's
    /// definitions by default. Unknown ids throw as the provider is evaluated.
    /// </summary>
    public Func<string, ContextIntProvider?> ResolveInt { get; init; } =
        id => ContextNumberProviders.Ints.GetValueOrDefault(id);

    /// <summary>
    /// Gets or sets how a provider id referenced by another provider resolves to a float provider; vanilla's
    /// definitions by default. Unknown ids throw as the provider is evaluated.
    /// </summary>
    public Func<string, ContextFloatProvider?> ResolveFloat { get; init; } =
        id => ContextNumberProviders.Floats.GetValueOrDefault(id);

    /// <summary>Evaluates an inline predicate or predicate id in the caller's domain.</summary>
    public Func<JsonElement, bool>? TestCondition { get; init; }

    // World-dependent sources are supplied by the caller, without coupling this component to a server or loot.
    /// <summary>
    /// Gets or sets the integer value of an environment attribute by id, for <c>minecraft:environment_attribute</c>
    /// providers; they give 0 when this is null or returns null.
    /// </summary>
    public Func<string, int?>? IntEnvironmentAttribute { get; init; }

    /// <summary>
    /// Gets or sets the float value of an environment attribute by id, for <c>minecraft:environment_attribute</c>
    /// providers; they give 0 when this is null or returns null.
    /// </summary>
    public Func<string, float?>? FloatEnvironmentAttribute { get; init; }

    /// <summary>
    /// Gets or sets the integer read from a command storage id and path, for <c>minecraft:storage</c> providers; they
    /// use their fallback when this is null or returns null.
    /// </summary>
    public Func<string, string, int?>? IntStorage { get; init; }

    /// <summary>
    /// Gets or sets the float read from a command storage id and path, for <c>minecraft:storage</c> providers; they use
    /// their fallback when this is null or returns null.
    /// </summary>
    public Func<string, string, float?>? FloatStorage { get; init; }

    /// <summary>
    /// Gets or sets the score of a JSON target for an objective name, for <c>minecraft:score</c> providers; they use
    /// their fallback when this is null or returns null.
    /// </summary>
    public Func<JsonElement, string, int?>? Score { get; init; }

    /// <summary>
    /// Gets the enchantment level <c>minecraft:enchantment_level</c> providers scale by; 0 when null.
    /// </summary>
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

    /// <summary>Evaluates a command expression, preserving arithmetic failures for command feedback.</summary>
    public int GetIntOrThrow(NumberContext context) => this.Evaluate(context);

    /// <summary>
    /// Evaluates the expression, giving 0 when its arithmetic fails (overflow or division by zero, for example).
    /// </summary>
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

    /// <summary>
    /// Evaluates a command expression, preserving arithmetic failures and rejecting non-finite output.
    /// </summary>
    public float GetFloatOrThrow(NumberContext context)
    {
        var value = this.Evaluate(context);
        if (!float.IsFinite(value))
            throw new ArithmeticException("The provider returned a non-finite value.");

        return value;
    }

    /// <summary>
    /// Evaluates the expression, giving 0 when its arithmetic fails or its result isn't finite.
    /// </summary>
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
