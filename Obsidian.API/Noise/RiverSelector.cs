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
        //return n > 0.15 ? 1 : Math.Max((Math.Cos(15 * n + Math.PI))/1.5d + 0.5, -0.1);
        return n > 0.6 ? 1 : Math.Max(3 * n + 0.07 * Math.Sin(40 * n) - 0.225, -0.1);
    }
}
