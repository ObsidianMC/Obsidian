using SharpNoise.Modules;

namespace Obsidian.API.Noise;

public class TransitionMap : Module
{
    /// <summary>
    /// Module that goes high as the source map transitions states.
    /// Should be cached.
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
    /// Detect transitions in source map.
    /// </summary>
    /// <returns>Progressively higher values (-1 < value < 1) as source transitions starting at Distance.</returns>
    public override double GetValue(double x, double y, double z)
    {

        var self = Source0.GetValue(x, y, z);
        double distance = Distance;
        var res = (
                (Source0.GetValue(x, y, z + distance) == self ? -1d : 1d) +
                (Source0.GetValue(x, y, z - distance) == self ? -1d : 1d) +
                (Source0.GetValue(x + distance, y, z + distance) == self ? -1d : 1d) +
                (Source0.GetValue(x - distance, y, z + distance) == self ? -1d : 1d) +
                (Source0.GetValue(x + distance, y, z - distance) == self ? -1d : 1d) +
                (Source0.GetValue(x - distance, y, z - distance) == self ? -1d : 1d) +
                (Source0.GetValue(x + distance, y, z) == self ? -1d : 1d) +
                (Source0.GetValue(x - distance, y, z) == self ? -1d : 1d)

            ) / 8d; // Get average
        return res;
    }
}
