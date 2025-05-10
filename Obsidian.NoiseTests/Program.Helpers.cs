using Obsidian.API.Noise;
using SharpNoise;
using SharpNoise.Builders;
using SharpNoise.Modules;
using SharpNoise.Utilities.Imaging;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.Noise;
using Obsidian.API;



public partial class Program
{
    private class Test2DNoiseModule(int sourceModuleCount) : Module(sourceModuleCount)
    {
        private readonly IDensityFunction fn = NoiseRegistry.NoiseSettings.Overworld.NoiseRouter.Ridges;
        public override double GetValue(double x, double y, double z)
        {
            double val = fn.GetValue(x, y, z);
            return val;
        }
    }

    private static void Test()
    {
        var noise = new Test2DNoiseModule(0);
        var map = new NoiseMap();
        var builder = new PlaneNoiseMapBuilder() { DestNoiseMap = map, SourceModule = noise };

        var image = new Image();
        var transitionsRenderer = new ImageRenderer() { SourceNoiseMap = map, DestinationImage = image };
        transitionsRenderer.BuildTerrainGradient();
        builder.SetBounds(-200, 200, -150, 150);
        builder.SetDestSize(200*2, 150*2);
        builder.Build();
        transitionsRenderer.Render();

        var bmp = transitionsRenderer.DestinationImage.ToGdiBitmap();
        bmp.Save("Ridges.bmp");
    }
}
