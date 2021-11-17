using SharpNoise.Modules;
using static SharpNoise.Modules.Curve;

namespace Obsidian.WorldData.Generators.Overworld.Carvers;

public class CavesCarver : BaseCarver
{
    public CavesCarver() : base()
    {
        var thing = new Curve
        {
            ControlPoints = new List<ControlPoint>()
                {
                     new Curve.ControlPoint(-1, -1),
                     new Curve.ControlPoint(-0.7, -0.5),
                     new Curve.ControlPoint(-0.4, -0.5),
                     new Curve.ControlPoint(1, 1),
                },
            Source0 = new Billow
            {
                Frequency = 18.12345,
                Seed = settings.Seed + 1,
                Quality = SharpNoise.NoiseQuality.Fast,
                OctaveCount = 6,
                Lacunarity = 1.2234,
                Persistence = 1.23
            }
        };

        this.Result = new ScalePoint
        {
            XScale = 1 / 1024.0,
            YScale = 1 / 384.0,
            ZScale = 1 / 1024.0,
            Source0 = thing
        };
    }
}
