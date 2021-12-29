using SharpNoise.Modules;

namespace Obsidian.API.Noise;

/// <summary>
/// Scale Results, then Apply Bias.
/// </summary>
public class SplitScaleBias : Module
{
    /// <summary>
    /// Source noise module
    /// </summary>
    public Module Source0 { get; set; } = new Constant { ConstantValue = 0 };

    /// <summary>
    /// Value added to Scaled result.
    /// </summary>
    public double Bias { get; set; } = 0D;

    /// <summary>
    /// Centerpoint of scaling
    /// </summary>
    public double Center { get; set; } = 0;

    /// <summary>
    /// Scaling to apply above centerpoint.
    /// </summary>
    public double AboveCenterScale = 1.0D;

    /// <summary>
    /// Scaling to apply below and including centerpoint.
    /// </summary>
    public double BelowCenterScale = 1.0D;

    /// <summary>
    /// ctor.
    /// </summary>
    public SplitScaleBias() : base(1)
    {

    }

    /// <summary>
    /// Retrieve noise value
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    /// <returns></returns>
    public override double GetValue(double x, double y, double z)
    {
        double val = Source0.GetValue(x, y, z);
        val *= val > Center ? AboveCenterScale : BelowCenterScale;
        return val + Bias;
    }
}
