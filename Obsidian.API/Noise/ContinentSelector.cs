using SharpNoise.Modules;

namespace Obsidian.API.Noise;

internal class ContinentSelector : Module
{
    public Module TerrainNoise { get; set; }
    public double ContinentOceanOffset { get; set; } = 0.00;
    public ContinentSelector() : base(1)
    {
    }

    public override double GetValue(double x, double y, double z)
    {
        // Y vals:
        // Deep Ocean < 0.6
        // Ocean < 0
        // Base land 0.03
        // Elevated Land 0.2
        // Mountain 0.5
        var noise = TerrainNoise.GetValue(x, y, z);
        return noise switch
        {
            double n when n <= -0.18991 => Math.Max(-1.0, Math.Pow(2*n+1, 3)-0.6) + ContinentOceanOffset,
            double n when n > -0.18991 && n <= 0.07646 => (Math.Pow(n, 3) * 50) + (0.1 * n) + ContinentOceanOffset,
            double n when n > 0.07646 && n <= 0.4487 => 0.03 + ContinentOceanOffset,
            double n when n > 0.4487 => Math.Min(Math.Pow(3 * n - 1.9, 3) + 0.2 + ContinentOceanOffset, 1),
        };
    }
}
