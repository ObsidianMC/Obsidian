namespace Obsidian.API;
public interface IDensityFunction : IRegistryResource
{
    public double MinValue { get; }
    public double MaxValue { get; }
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
