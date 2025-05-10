using Obsidian.API.Noise;

namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:old_blended_noise")]
public class OldBlendedNoiseDensityFunction : IDensityFunction
{
    public int Seed { get; init; }

    public virtual required double SmearScaleMultiplier { get; init; }

    public virtual required double XzFactor { get; init; }

    public virtual required double XzScale { get; init; }

    public virtual required double YFactor { get; init; }

    public virtual required double YScale { get; init; }

    public string Type => "minecraft:old_blended_noise";

    public double MinValue => -MaxValue;

    public double MaxValue
    {
        get
        {
            if (!_initialized)
            {
                Create();
            }
            return field;
        }
        private set;
    }

    private bool _initialized = false;
    private PerlinNoise MinLimitNoise;
    private PerlinNoise MaxLimitNoise;
    private PerlinNoise MainNoise;
    private double XzMultiplier;
    private double YMultiplier;


    public void Create()
    {
        MaxValue = 3.0D;

        var minConfig = PerlinNoise.CreateConfigFromOctaves(Enumerable.Range(-15, 16).ToList());
        MinLimitNoise = new PerlinNoise(new Random(Seed + 10), minConfig);

        var maxConfig = PerlinNoise.CreateConfigFromOctaves(Enumerable.Range(-15, 16).ToList());
        MaxLimitNoise = new PerlinNoise(new Random(Seed + 11), minConfig);

        var mainConfig = PerlinNoise.CreateConfigFromOctaves(Enumerable.Range(-7, 8).ToList());
        MainNoise = new PerlinNoise(new Random(Seed + 12), minConfig);        

        XzMultiplier = 684.412 * XzScale;
        YMultiplier = 684.412 * YScale;
        MaxValue = MinLimitNoise.MaxBrokenValue(YMultiplier);
        _initialized = true;
    }
    public virtual double GetValue(double x, double y, double z)
    {
        if (!_initialized)
        {
            Create();
        }

        double blockX = x * XzMultiplier;
        double blockY = y * YMultiplier;
        double blockZ = z * XzMultiplier;
        double scaledX = blockX / XzFactor;
        double scaledY = blockY / YFactor;
        double scaledZ = blockZ / XzFactor;
        double smearYScale = YMultiplier * SmearScaleMultiplier;
        double smearYFactor = smearYScale / YFactor;

        double minValue = 0.0;
        double maxValue = 0.0;
        double mainNoiseValue = 0.0;
        double octaveScale = 1.0;

        for (int i = 0; i < 8; i++)
        {
            var octaveNoise = MainNoise.GetOctaveNoise(i);
            if (octaveNoise != null)
            {
                mainNoiseValue += octaveNoise.Noise(
                    PerlinNoise.Wrap(scaledX * octaveScale),
                    PerlinNoise.Wrap(scaledY * octaveScale),
                    PerlinNoise.Wrap(scaledZ * octaveScale),
                    smearYFactor * octaveScale,
                    scaledY * octaveScale
                ) / octaveScale;
            }

            octaveScale /= 2.0;
        }

        double normalizedMainNoise = (mainNoiseValue / 10.0 + 1.0) / 2.0;
        bool aboveMax = normalizedMainNoise >= 1.0;
        bool belowMin = normalizedMainNoise <= 0.0;

        octaveScale = 1.0;

        for (int j = 0; j < 16; j++)
        {
            double octaveX = PerlinNoise.Wrap(blockX * octaveScale);
            double octaveY = PerlinNoise.Wrap(blockY * octaveScale);
            double octaveZ = PerlinNoise.Wrap(blockZ * octaveScale);
            double octaveYScale = smearYScale * octaveScale;

            if (!aboveMax)
            {
                minValue += MinLimitNoise.GetOctaveNoise(j).Noise(octaveX, octaveY, octaveZ, octaveYScale, blockY * octaveScale) / octaveScale;
            }

            if (!belowMin)
            {
                maxValue += MaxLimitNoise.GetOctaveNoise(j).Noise(octaveX, octaveY, octaveZ, octaveYScale, blockY * octaveScale) / octaveScale;
            }

            octaveScale /= 2.0;
        }
        var result = normalizedMainNoise < 0.0 ? minValue / 512.0 : normalizedMainNoise > 1.0 ? maxValue / 512.0 : MathUtils.Lerp(normalizedMainNoise, minValue / 512.0, maxValue / 512.0);
        return  result / 128.0;
    }
}
