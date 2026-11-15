using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API;

/// <summary>
/// Samples a float, like vanilla's FloatProvider.
/// </summary>
public interface IFloatProvider : IRegistryResource
{
    /// <summary>
    /// Gets the provider's type identifier, like <c>minecraft:uniform</c>.
    /// </summary>
    public new string Type { get; }

    /// <summary>
    /// Gets the smallest value <see cref="Sample"/> can return.
    /// </summary>
    public float MinValue { get; }

    /// <summary>
    /// Gets the largest value <see cref="Sample"/> can return.
    /// </summary>
    public float MaxValue { get; }

    /// <summary>
    /// Draws a value.
    /// </summary>
    /// <param name="random">The random source to draw from.</param>
    /// <returns>The value drawn.</returns>
    public float Sample(IRandomSource random);
}
