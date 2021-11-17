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
        double strength = 1.5;
        if (self <= 0) { strength = 0.5; }
        var values = new double[8]
        {
                Source0.GetValue(x, y, z + Distance) == self ? -1 : strength,
                Source0.GetValue(x, y, z - Distance) == self ? -1 : strength,
                Source0.GetValue(x + Distance, y, z) == self ? -1 : strength,
                Source0.GetValue(x - Distance, y, z) == self ? -1 : strength,
                Source0.GetValue(x + Distance, y, z + Distance) == self ? -1 : (strength * 0.75),
                Source0.GetValue(x - Distance, y, z - Distance) == self ? -1 : (strength * 0.75),
                Source0.GetValue(x + Distance, y, z - Distance) == self ? -1 : (strength * 0.75),
                Source0.GetValue(x - Distance, y, z + Distance) == self ? -1 : (strength * 0.75),
        };

        return values.Average();
    }
}
