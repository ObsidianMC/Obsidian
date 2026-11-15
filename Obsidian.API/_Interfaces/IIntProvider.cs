using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API;

/// <summary>
/// Samples an int, like vanilla's IntProvider.
/// </summary>
public interface IIntProvider : IRegistryResource
{
    /// <summary>
    /// Can be constant, uniform, biased_to_bottom, very_biased_to_bottom, clamped, clamped_normal,
    /// weighted_list, or trapezoid.
    /// </summary>
    /// <remarks>
    /// See <see cref="IntProviderTypes"/> for the default types.
    /// </remarks>
    public new string Type { get; }

    /// <summary>
    /// Gets the smallest value <see cref="Sample"/> can return.
    /// </summary>
    public int MinValue { get; }

    /// <summary>
    /// Gets the largest value <see cref="Sample"/> can return.
    /// </summary>
    public int MaxValue { get; }

    /// <summary>
    /// Draws a value.
    /// </summary>
    /// <param name="random">The random source to draw from.</param>
    /// <returns>The value drawn.</returns>
    public int Sample(IRandomSource random);
}

/// <summary>
/// The identifiers of vanilla's int provider types.
/// </summary>
public static class IntProviderTypes
{
    /// <summary>
    /// Always the same value.
    /// </summary>
    public const string Constant = "minecraft:constant";
    /// <summary>
    /// Any value in a range, equally likely.
    /// </summary>
    public const string Uniform = "minecraft:uniform";
    /// <summary>
    /// A value in a range, more likely towards the bottom.
    /// </summary>
    public const string BiasedToBottom = "minecraft:biased_to_bottom";
    /// <summary>
    /// A value in a range, much more likely towards the bottom.
    /// </summary>
    public const string VeryBiasedToBottom = "minecraft:very_biased_to_bottom";
    /// <summary>
    /// Another provider's value, clamped to a range.
    /// </summary>
    public const string Clamped = "minecraft:clamped";
    /// <summary>
    /// A normally distributed value, clamped to a range.
    /// </summary>
    public const string ClampedNormal = "minecraft:clamped_normal";
    /// <summary>
    /// One of several providers, picked by weight.
    /// </summary>
    public const string WeightedList = "minecraft:weighted_list";
    /// <summary>
    /// A value in a range with a trapezoidal distribution.
    /// </summary>
    public const string Trapezoid = "minecraft:trapezoid";
}
