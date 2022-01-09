using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Generators.Overworld;
using SharpNoise;
using SharpNoise.Builders;
using SharpNoise.Utilities.Imaging;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public class Noise
{
    [Fact(DisplayName = "WorldGen", Timeout = 100)]
    public async void SameAsync()
    {
        await Task.Run(() =>
        {
            OverworldGenerator og = new OverworldGenerator("1");
            OverworldTerrain noiseGen = new OverworldTerrain(true);

            var map = new NoiseMap();

            PlaneNoiseMapBuilder builder =
                new PlaneNoiseMapBuilder() { DestNoiseMap = map, SourceModule = noiseGen.Result };

            var image = new SharpNoise.Utilities.Imaging.Image();
            var renderer = new ImageRenderer() { SourceNoiseMap = map, DestinationImage = image };

                //renderer.BuildGrayscaleGradient();
                renderer.BuildTerrainGradient();

            builder.SetBounds(-2000, 2000, -2000, 2000);
            builder.SetDestSize(4000, 4000);
            builder.Build();

            renderer.Render();

            var bmp = renderer.DestinationImage.ToGdiBitmap();
            bmp.Save("terrain.bmp");

            Assert.Equal(0, 0);
        });
    }
}
