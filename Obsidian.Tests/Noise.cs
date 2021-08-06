using Obsidian.WorldData.Generators.Overworld;
using Obsidian.WorldData.Generators.Overworld.Terrain;
using SharpNoise;
using SharpNoise.Builders;
using SharpNoise.Utilities.Imaging;
using Xunit;

namespace Obsidian.Tests
{
    public class Noise
    {
        [Fact(DisplayName = "WorldGen", Timeout = 100)]
        public async void SameAsync()
        {
            OverworldTerrainSettings generatorSettings = new OverworldTerrainSettings();
            generatorSettings.Seed = 137;
            OverworldTerrain noiseGen = new OverworldTerrain(generatorSettings, true);

            var map = new NoiseMap();

            PlaneNoiseMapBuilder builder = new PlaneNoiseMapBuilder()
            {
                DestNoiseMap = map,
                SourceModule = noiseGen.Result
            };

            var image = new SharpNoise.Utilities.Imaging.Image();
            var renderer = new ImageRenderer()
            {
                SourceNoiseMap = map,
                DestinationImage = image
            };

            //renderer.BuildGrayscaleGradient();
            renderer.BuildTerrainGradient();

            builder.SetBounds(-200, 200, -0, 400);
            builder.SetDestSize(400, 400);
            builder.Build();

            renderer.Render();

            var bmp = renderer.DestinationImage.ToGdiBitmap();
            bmp.Save("terrain.bmp");

            Assert.Equal(0, 0);
        }
    }
}