using SharpNoise.Modules;

namespace Obsidian.API.Noise;

public class TransitionMap : Module
{
    /// <summary>
    /// Module that goes high as the source map transitions states
    /// </summary>
    public Module Source0 { get; set; }

    public int Distance { get; set; } = 1;

    /// <summary>
    /// ctor.
    /// </summary>
    public TransitionMap(Module source0, int distance) : base(1)
    {
        Source0 = source0;
        Distance = distance;
    }

    /// <summary>
    /// Perform blur.
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    /// <returns></returns>
    public override double GetValue(double x, double y, double z)
    {
        var self = Source0.GetValue(x, y, z);
        double strength = self > 0 ? 1.5 : 0.5;
        double strength75 = strength * 0.75;
        double distance = Distance;

        return (

                Source0.GetValue(x, y, z + distance) == self ? -1d : strength +
                Source0.GetValue(x, y, z - distance) == self ? -1d : strength +
                Source0.GetValue(x + distance, y, z) == self ? -1d : strength +
                Source0.GetValue(x - distance, y, z) == self ? -1d : strength +
                Source0.GetValue(x + distance, y, z + distance) == self ? -1d : strength75 +
                Source0.GetValue(x - distance, y, z - distance) == self ? -1d : strength75 +
                Source0.GetValue(x + distance, y, z - distance) == self ? -1d : strength75 +
                Source0.GetValue(x - distance, y, z + distance) == self ? -1d : strength75

            ) / 8d; // Get average
    }
}
