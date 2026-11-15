using SharpNoise.Modules;

namespace Obsidian.API.Noise;

/// <summary>
/// Shapes the absolute source noise into a river profile with a bounded channel floor.
/// </summary>
public class RiverSelector : Module
{
    /// <summary>
    /// Gets or sets the source module used to calculate the river profile.
    /// </summary>
    public Module RiverNoise { get; set; }
    /// <summary>
    /// Creates a river profile module whose source is supplied through <see cref="RiverNoise"/>.
    /// </summary>
    public RiverSelector() : base(0)
    {

    }

    /// <summary>
    /// Transforms the absolute source sample into the river profile, returning 1.5 above the cutoff.
    /// </summary>
    public override double GetValue(double x, double y, double z)
    {
        var n = Math.Abs(RiverNoise.GetValue(x, y, z));
        // Desmos: 5x\ +\ 0.1\sin\left(50x+1\right)\ -0.19\ \left\{0\ <\ x\right\}
        return n >= 0.3484 ? 1.5 : Math.Max(5 * n + 0.1 * Math.Sin(50 * n + 0.75) - 0.19, -0.085);
    }
}
