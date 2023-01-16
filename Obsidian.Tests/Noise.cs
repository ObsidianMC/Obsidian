using Obsidian.API;
using Obsidian.API.Noise;
using Obsidian.WorldData.Generators.Overworld;
using Org.BouncyCastle.Crypto.Engines;
using SharpNoise;
using SharpNoise.Builders;
using SharpNoise.Utilities.Imaging;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public class Noise
{
    private OverworldTerrainNoise noiseGen = new OverworldTerrainNoise(654321, true);

    [Fact(DisplayName = "Biomes", Timeout = 10000)]
    public async void BiomesAsync()
    {

        await Task.Run(() =>
        {
            var map = new NoiseMap();
            var builder = new PlaneNoiseMapBuilder() { DestNoiseMap = map, SourceModule = noiseGen.Biome };
            var image = new Image();
            var biomesRenderer = new ImageRenderer() { SourceNoiseMap = map, DestinationImage = image };

            biomesRenderer.AddGradientPoint((int)Biomes.TheVoid, new Color(0, 0, 0, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.Plains, new Color(86, 125, 70, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.SunflowerPlains, new Color(255, 196, 63, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.SnowyPlains, new Color(200, 200, 255, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.IceSpikes, new Color(100, 100, 255, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.Desert, new Color(128, 128, 0, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.Swamp, new Color(0, 128, 0, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.Forest, new Color(32, 255, 32, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.FlowerForest, new Color(96, 255, 0, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.BirchForest, new Color(182, 255, 182, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.DarkForest, new Color(0, 100, 0, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.OldGrowthBirchForest, new Color(150, 255, 150, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.OldGrowthPineTaiga, new Color(1, 121, 111, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.OldGrowthSpruceTaiga, new Color(105, 193, 126, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.Taiga, new Color(118, 128, 120, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.SnowyTaiga, new Color(218, 228, 220, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.Savanna, new Color(209, 163, 110, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.SavannaPlateau, new Color(224, 190, 146, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.WindsweptHills, new Color(11, 102, 35, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.WindsweptGravellyHills, new Color(128, 164, 128, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.WindsweptForest, new Color(11, 102, 35, 196));
            biomesRenderer.AddGradientPoint((int)Biomes.WindsweptSavanna, new Color(209, 163, 110, 196));
            biomesRenderer.AddGradientPoint((int)Biomes.Jungle, new Color(41, 171, 135, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.SparseJungle, new Color(41, 171, 135, 196));
            biomesRenderer.AddGradientPoint((int)Biomes.BambooJungle, new Color(221, 202, 133, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.Badlands, new Color(167, 161, 143, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.ErodedBadlands, new Color(167, 161, 143, 196));
            biomesRenderer.AddGradientPoint((int)Biomes.WoodedBadlands, new Color(167, 224, 143, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.Meadow, new Color(48, 186, 143, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.Grove, new Color(120, 127, 86, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.SnowySlopes, new Color(144, 184, 212, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.FrozenPeaks, new Color(144, 184, 255, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.JaggedPeaks, new Color(128, 128, 128, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.StonyPeaks, new Color(128, 128, 128, 196));
            biomesRenderer.AddGradientPoint((int)Biomes.River, new Color(38, 102, 145, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.FrozenRiver, new Color(186, 218, 232, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.Beach, new Color(248, 220, 172, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.SnowyBeach, new Color(248, 220, 255, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.StonyShore, new Color(170, 170, 170, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.WarmOcean, new Color(60, 181, 177, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.LukewarmOcean, new Color(40, 181, 177, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.DeepLukewarmOcean, new Color(40, 181, 177, 128));
            biomesRenderer.AddGradientPoint((int)Biomes.Ocean, new Color(0, 105, 148, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.DeepOcean, new Color(0, 105, 148, 128));
            biomesRenderer.AddGradientPoint((int)Biomes.ColdOcean, new Color(107, 197, 198, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.DeepColdOcean, new Color(107, 197, 198, 128));
            biomesRenderer.AddGradientPoint((int)Biomes.FrozenOcean, new Color(163, 191, 203, 255));
            biomesRenderer.AddGradientPoint((int)Biomes.DeepFrozenOcean, new Color(163, 191, 203, 128));
            biomesRenderer.AddGradientPoint((int)Biomes.MushroomFields, new Color(119, 103, 84, 255));


            builder.SetBounds(-800, 800, -600, 600);
            builder.SetDestSize(1600, 1200);
            builder.Build();
            biomesRenderer.Render();

            var bmp = biomesRenderer.DestinationImage.ToGdiBitmap();
            bmp.Save("_biomes.bmp");

            Assert.Equal(0, 0);
        });
    }

    [Fact(DisplayName = "Terrain", Timeout = 1000000)]
    public async void TerrainAsync()
    {
        
        await Task.Run(() =>
        {
            NoiseCube nc = new();
            NoiseMap nm = new();

            LinearNoiseCubeBuilder lncb = new()
            {
                DestNoiseCube = nc,
                SourceModule = noiseGen.terrain
            };
            lncb.SetBounds(-800, 800, 0, 320, -600, 600);
            lncb.SetDestSize(1600, 320, 1200);
            lncb.Build();

            HeightNoiseMapBuilder dnmb = new()
            {
                DestNoiseMap = nm,
                SourceNoiseCube = nc
            };
            dnmb.SetDestSize(1600, 1200);
            dnmb.Build();

            Image img = new();
            ImageRenderer transitionsRenderer = new()
            {
                SourceNoiseMap = nm,
                DestinationImage = img
            };

            transitionsRenderer.BuildTerrainGradient();
            transitionsRenderer.Render();

            var bmp = transitionsRenderer.DestinationImage.ToGdiBitmap();
            bmp.Save("_terrain.bmp");

            Assert.Equal(0, 0);
        });
    }

    [Fact(DisplayName = "Temp", Timeout = 10000)]
    public async void TempAsync()
    {
        await Task.Run(() =>
        {
            var noise = noiseGen.TemperaturePerlin;
            var map = new NoiseMap();
            PlaneNoiseMapBuilder builder =
                new PlaneNoiseMapBuilder() { DestNoiseMap = map, SourceModule = noise };

            var image = new Image();
            var transitionsRenderer = new ImageRenderer() { SourceNoiseMap = map, DestinationImage = image };
            transitionsRenderer.BuildGrayscaleGradient();
            builder.SetBounds(-800, 800, -600, 600);
            builder.SetDestSize(1600, 1200);
            builder.Build();
            transitionsRenderer.Render();

            var bmp = transitionsRenderer.DestinationImage.ToGdiBitmap();
            bmp.Save("_temp.bmp");

            Assert.Equal(0, 0);
        });
    }

    [Fact(DisplayName = "Height", Timeout = 10000)]
    public async void HeightAsync()
    {
        await Task.Run(() =>
        {
            var noise = noiseGen.HeightPerlin;
            var map = new NoiseMap();
            PlaneNoiseMapBuilder builder =
                new PlaneNoiseMapBuilder() { DestNoiseMap = map, SourceModule = noise };

            var image = new Image();
            var transitionsRenderer = new ImageRenderer() { SourceNoiseMap = map, DestinationImage = image };
            transitionsRenderer.BuildTerrainGradient();
            builder.SetBounds(-800, 800, -600, 600);
            builder.SetDestSize(1600, 1200);
            builder.Build();
            transitionsRenderer.Render();

            var bmp = transitionsRenderer.DestinationImage.ToGdiBitmap();
            bmp.Save("_height.bmp");

            Assert.Equal(0, 0);
        });
    }

    [Fact(DisplayName = "Erosion", Timeout = 10000)]
    public async void ErosionAsync()
    {
        await Task.Run(() =>
        {
            var noise = noiseGen.SquashNoise;
            var map = new NoiseMap();
            PlaneNoiseMapBuilder builder =
                new PlaneNoiseMapBuilder() { DestNoiseMap = map, SourceModule = noise };

            var image = new Image();
            var transitionsRenderer = new ImageRenderer() { SourceNoiseMap = map, DestinationImage = image };
            transitionsRenderer.BuildGrayscaleGradient();
            builder.SetBounds(-800, 800, -600, 600);
            builder.SetDestSize(1600, 1200);
            builder.Build();
            transitionsRenderer.Render();

            var bmp = transitionsRenderer.DestinationImage.ToGdiBitmap();
            bmp.Save("_erosion.bmp");

            Assert.Equal(0, 0);
        });
    }

    [Fact(DisplayName = "Humidity", Timeout = 10000)]
    public async void HumidityAsync()
    {
        await Task.Run(() =>
        {
            var noise = noiseGen.HumidityPerlin;
            var map = new NoiseMap();
            PlaneNoiseMapBuilder builder =
                new PlaneNoiseMapBuilder() { DestNoiseMap = map, SourceModule = noise };

            var image = new Image();
            var transitionsRenderer = new ImageRenderer() { SourceNoiseMap = map, DestinationImage = image };
            transitionsRenderer.BuildGrayscaleGradient();
            builder.SetBounds(-800, 800, -600, 600);
            builder.SetDestSize(1600, 1200);
            builder.Build();
            transitionsRenderer.Render();

            var bmp = transitionsRenderer.DestinationImage.ToGdiBitmap();
            bmp.Save("_hummidity.bmp");

            Assert.Equal(0, 0);
        });
    }
}
