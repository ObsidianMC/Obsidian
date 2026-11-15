namespace Obsidian.API;
/// <summary>
/// A worldgen noise, like vanilla's noise parameters.
/// </summary>
public interface INoise : IRegistryResource
{
    /// <summary>
    /// Gets the smallest value the noise can return.
    /// </summary>
    public double MinValue { get; }
    /// <summary>
    /// Gets the largest value the noise can return.
    /// </summary>
    public double MaxValue { get; }
    /// <summary>
    /// Builds the noise's generator from its parameters. It is built the first time it's needed if this isn't called.
    /// </summary>
    public void Create();
    /// <summary>
    /// Samples the noise at a position.
    /// </summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <param name="z">The z coordinate.</param>
    /// <returns>The value, between <see cref="MinValue"/> and <see cref="MaxValue"/>.</returns>
    double GetValue(double x, double y, double z);
}
