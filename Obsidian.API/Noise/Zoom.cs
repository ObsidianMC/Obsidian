using SharpNoise.Modules;

namespace Obsidian.API.Noise;

public class Zoom : Module
{
    public Module Source0 { get; set; }

    public double Amount { get; set; }

    public Zoom(Module source0, double amount) : base(1)
    {
        Source0 = source0;
        Amount = amount;
    }

    public override double GetValue(double x, double y, double z)
    {
        return Source0.GetValue(x / Amount, y / Amount, z / Amount);
    }
}
