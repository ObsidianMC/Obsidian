using SharpNoise.Modules;
using System;

namespace Obsidian.API.Noise;

/// <summary>
/// Usefull for zooming.
/// Should be cached.
/// </summary>
public class BitShiftInput : Module
{
    /// <summary>
    /// Source noise module
    /// </summary>
    public Module Source0 { get; set; }

    /// <summary>
    /// Amount to bitshift.
    /// </summary>
    public int Amount { get; set; }

    /// <summary>
    /// Whether to shift left, or right.
    /// </summary>
    public bool Left { get; set; }

    /// <summary>
    /// ctor.
    /// </summary>
    public BitShiftInput(Module source0) : base(1)
    {
        Source0 = source0;
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
        x = Left ? (int)Math.Floor(x) << Amount : (int)Math.Floor(x) >> Amount;
        y = Left ? (int)Math.Floor(y) << Amount : (int)Math.Floor(y) >> Amount;
        z = Left ? (int)Math.Floor(z) << Amount : (int)Math.Floor(z) >> Amount;
        return Source0.GetValue(x, y, z);
    }
}
