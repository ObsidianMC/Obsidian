using SharpNoise.Modules;

namespace Obsidian.API.Noise;

public class Blend : Module
{
    /// <summary>
    /// Source to blur. Expensive ops should be cached.
    /// </summary>
    public Module Source0 { get; set; }

    /// <summary>
    /// 
    /// </summary>
    public int Distance { get; set; } = 1;

    /// <summary>
    /// ctor.
    /// </summary>
    public Blend(Module source0) : base(1)
    {
        Source0 = source0;
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
        var values = new double[5]
        {
                self,
                Source0.GetValue(x, y, z + Distance),
                Source0.GetValue(x, y, z - Distance),
                Source0.GetValue(x + Distance, y, z),
                Source0.GetValue(x - Distance, y, z),
        };

        return values.Average();
    }
}
