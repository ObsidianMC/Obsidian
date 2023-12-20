using SharpNoise.Modules;

namespace Obsidian.API.Noise;

public class RiverSelector : Module
{
    public Module RiverNoise { get; set; }
    public RiverSelector() : base(0)
    {

    }

    public override double GetValue(double x, double y, double z)
    {
        var n = Math.Abs(RiverNoise.GetValue(x, y, z));
        // Desmos: 5x\ +\ 0.1\sin\left(50x+1\right)\ -0.19\ \left\{0\ <\ x\right\}
        return n >= 0.3484 ? 1.5 : Math.Max(5 * n + 0.1 * Math.Sin(50 * n + 0.75) - 0.19, -0.085);
    }
}
