namespace Obsidian.API;
/// <summary>
/// A worldgen density function, like vanilla's DensityFunction: a value for every point that decides terrain shape.
/// </summary>
public interface IDensityFunction : IRegistryResource
{
    /// <summary>
    /// Gets the smallest value the function can return.
    /// </summary>
    public double MinValue { get; }
    /// <summary>
    /// Gets the largest value the function can return.
    /// </summary>
    public double MaxValue { get; }
    /// <summary>
    /// Computes the function's value at a block position.
    /// </summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <param name="z">The z coordinate.</param>
    /// <returns>The value, between <see cref="MinValue"/> and <see cref="MaxValue"/>.</returns>
    public double GetValue(double x, double y, double z);

    /// <summary>
    /// Rebuilds this function with every child passed through <paramref name="visitor"/> (depth-first)
    /// and then hands the rebuilt function itself to the visitor.
    /// </summary>
    /// <remarks>
    /// Used to bind seeded noises to a world and to swap marker functions (interpolated, caches)
    /// for chunk-bound implementations. Leaf functions only need the default implementation.
    /// </remarks>
    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) => visitor.Apply(this);
}
