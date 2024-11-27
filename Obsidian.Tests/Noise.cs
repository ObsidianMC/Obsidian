using Obsidian.API;
using Obsidian.API.Noise;
using Obsidian.WorldData.Generators.Overworld;
using SharpNoise;
using SharpNoise.Builders;
using SharpNoise.Utilities.Imaging;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public class Noise
{
    private OverworldTerrainNoise noiseGen = new OverworldTerrainNoise(0);

    [Fact(DisplayName = "Biomes", Timeout = 10000)]
    public async void BiomesAsync()
    {

        await Task.Run(() =>
        {
            var map = new NoiseMap();
            var builder = new PlaneNoiseMapBuilder() { DestNoiseMap = map, SourceModule = noiseGen.Biome };
            var image = new Image();
            var biomesRenderer = new ImageRenderer() { SourceNoiseMap = map, DestinationImage = image };

            biomesRenderer.AddGradientPoint((int)Biome.TheVoid, new Color(0, 0, 0, 255));
            biomesRenderer.AddGradientPoint((int)Biome.Plains, new Color(86, 125, 70, 255));
            biomesRenderer.AddGradientPoint((int)Biome.SunflowerPlains, new Color(255, 196, 63, 255));
            biomesRenderer.AddGradientPoint((int)Biome.SnowyPlains, new Color(200, 200, 255, 255));
            biomesRenderer.AddGradientPoint((int)Biome.IceSpikes, new Color(100, 100, 255, 255));
            biomesRenderer.AddGradientPoint((int)Biome.Desert, new Color(128, 128, 0, 255));
            biomesRenderer.AddGradientPoint((int)Biome.Swamp, new Color(0, 128, 0, 255));
            biomesRenderer.AddGradientPoint((int)Biome.Forest, new Color(32, 255, 32, 255));
            biomesRenderer.AddGradientPoint((int)Biome.FlowerForest, new Color(96, 255, 0, 255));
            biomesRenderer.AddGradientPoint((int)Biome.BirchForest, new Color(182, 255, 182, 255));
            biomesRenderer.AddGradientPoint((int)Biome.DarkForest, new Color(0, 100, 0, 255));
            biomesRenderer.AddGradientPoint((int)Biome.OldGrowthBirchForest, new Color(150, 255, 150, 255));
            biomesRenderer.AddGradientPoint((int)Biome.OldGrowthPineTaiga, new Color(1, 121, 111, 255));
            biomesRenderer.AddGradientPoint((int)Biome.OldGrowthSpruceTaiga, new Color(105, 193, 126, 255));
            biomesRenderer.AddGradientPoint((int)Biome.Taiga, new Color(118, 128, 120, 255));
            biomesRenderer.AddGradientPoint((int)Biome.SnowyTaiga, new Color(218, 228, 220, 255));
            biomesRenderer.AddGradientPoint((int)Biome.Savanna, new Color(209, 163, 110, 255));
            biomesRenderer.AddGradientPoint((int)Biome.SavannaPlateau, new Color(224, 190, 146, 255));
            biomesRenderer.AddGradientPoint((int)Biome.WindsweptHills, new Color(11, 102, 35, 255));
            biomesRenderer.AddGradientPoint((int)Biome.WindsweptGravellyHills, new Color(128, 164, 128, 255));
            biomesRenderer.AddGradientPoint((int)Biome.WindsweptForest, new Color(11, 102, 35, 196));
            biomesRenderer.AddGradientPoint((int)Biome.WindsweptSavanna, new Color(209, 163, 110, 196));
            biomesRenderer.AddGradientPoint((int)Biome.Jungle, new Color(41, 171, 135, 255));
            biomesRenderer.AddGradientPoint((int)Biome.SparseJungle, new Color(41, 171, 135, 196));
            biomesRenderer.AddGradientPoint((int)Biome.BambooJungle, new Color(221, 202, 133, 255));
            biomesRenderer.AddGradientPoint((int)Biome.Badlands, new Color(167, 161, 143, 255));
            biomesRenderer.AddGradientPoint((int)Biome.ErodedBadlands, new Color(167, 161, 143, 196));
            biomesRenderer.AddGradientPoint((int)Biome.WoodedBadlands, new Color(167, 224, 143, 255));
            biomesRenderer.AddGradientPoint((int)Biome.Meadow, new Color(48, 186, 143, 255));
            biomesRenderer.AddGradientPoint((int)Biome.Grove, new Color(120, 127, 86, 255));
            biomesRenderer.AddGradientPoint((int)Biome.SnowySlopes, new Color(144, 184, 212, 255));
            biomesRenderer.AddGradientPoint((int)Biome.FrozenPeaks, new Color(144, 184, 255, 255));
            biomesRenderer.AddGradientPoint((int)Biome.JaggedPeaks, new Color(128, 128, 128, 255));
            biomesRenderer.AddGradientPoint((int)Biome.StonyPeaks, new Color(128, 128, 128, 196));
            biomesRenderer.AddGradientPoint((int)Biome.River, new Color(38, 102, 145, 255));
            biomesRenderer.AddGradientPoint((int)Biome.FrozenRiver, new Color(186, 218, 232, 255));
            biomesRenderer.AddGradientPoint((int)Biome.Beach, new Color(248, 220, 172, 255));
            biomesRenderer.AddGradientPoint((int)Biome.SnowyBeach, new Color(248, 220, 255, 255));
            biomesRenderer.AddGradientPoint((int)Biome.StonyShore, new Color(170, 170, 170, 255));
            biomesRenderer.AddGradientPoint((int)Biome.WarmOcean, new Color(60, 181, 177, 255));
            biomesRenderer.AddGradientPoint((int)Biome.LukewarmOcean, new Color(40, 181, 177, 255));
            biomesRenderer.AddGradientPoint((int)Biome.DeepLukewarmOcean, new Color(40, 181, 177, 128));
            biomesRenderer.AddGradientPoint((int)Biome.Ocean, new Color(0, 105, 148, 255));
            biomesRenderer.AddGradientPoint((int)Biome.DeepOcean, new Color(0, 105, 148, 128));
            biomesRenderer.AddGradientPoint((int)Biome.ColdOcean, new Color(107, 197, 198, 255));
            biomesRenderer.AddGradientPoint((int)Biome.DeepColdOcean, new Color(107, 197, 198, 128));
            biomesRenderer.AddGradientPoint((int)Biome.FrozenOcean, new Color(163, 191, 203, 255));
            biomesRenderer.AddGradientPoint((int)Biome.DeepFrozenOcean, new Color(163, 191, 203, 128));
            biomesRenderer.AddGradientPoint((int)Biome.MushroomFields, new Color(119, 103, 84, 255));


            builder.SetBounds(0, 1600, 0, 1200);
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
                SourceModule = noiseGen.TerrainSelector
            };
            lncb.SetBounds(0, 1600, -64, 320, 0, 1200);
            lncb.SetDestSize(1600, 384, 1200);
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

    [Fact(DisplayName = "Peaks", Timeout = 10000)]
    public async void PeaksAsync()
    {
        await Task.Run(() =>
        {
            var noise = noiseGen.PeakValleyNoise;
            var map = new NoiseMap();
            PlaneNoiseMapBuilder builder =
                new PlaneNoiseMapBuilder() { DestNoiseMap = map, SourceModule = noise };

            var image = new Image();
            var transitionsRenderer = new ImageRenderer() { SourceNoiseMap = map, DestinationImage = image };
            transitionsRenderer.BuildGrayscaleGradient();
            builder.SetBounds(0, 1600, 0, 1200);
            builder.SetDestSize(1600, 1200);
            builder.Build();
            transitionsRenderer.Render();

            var bmp = transitionsRenderer.DestinationImage.ToGdiBitmap();
            bmp.Save("_peaks.bmp");

            Assert.Equal(0, 0);
        });
    }

    [Fact(DisplayName = "Temp", Timeout = 10000)]
    public async void TempAsync()
    {
        await Task.Run(() =>
        {
            var noise = noiseGen.TempNoise;
            var map = new NoiseMap();
            PlaneNoiseMapBuilder builder =
                new PlaneNoiseMapBuilder() { DestNoiseMap = map, SourceModule = noise };

            var image = new Image();
            var transitionsRenderer = new ImageRenderer() { SourceNoiseMap = map, DestinationImage = image };
            transitionsRenderer.BuildGrayscaleGradient();
            builder.SetBounds(0, 1600, 0, 1200);
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
            var noise = noiseGen.HeightNoise;
            var map = new NoiseMap();
            PlaneNoiseMapBuilder builder =
                new PlaneNoiseMapBuilder() { DestNoiseMap = map, SourceModule = noise };

            var image = new Image();
            var transitionsRenderer = new ImageRenderer() { SourceNoiseMap = map, DestinationImage = image };
            transitionsRenderer.BuildTerrainGradient();
            builder.SetBounds(0, 1600, 0, 1200);
            builder.SetDestSize(1600, 1200);
            builder.Build();
            transitionsRenderer.Render();

            var bmp = transitionsRenderer.DestinationImage.ToGdiBitmap();
            bmp.Save("_height.bmp");

            Assert.Equal(0, 0);
        });
    }

    [Fact(DisplayName = "Squash", Timeout = 10000)]
    public async void SquashAsync()
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
            builder.SetBounds(0, 1600, 0, 1200);
            builder.SetDestSize(1600, 1200);
            builder.Build();
            transitionsRenderer.Render();

            var bmp = transitionsRenderer.DestinationImage.ToGdiBitmap();
            bmp.Save("_squash.bmp");

            Assert.Equal(0, 0);
        });
    }

    [Fact(DisplayName = "Rivers", Timeout = 10000)]
    public async void RiverAsync()
    {
        await Task.Run(() =>
        {
            var noise = noiseGen.RiverNoise;
            var map = new NoiseMap();
            PlaneNoiseMapBuilder builder =
                new PlaneNoiseMapBuilder() { DestNoiseMap = map, SourceModule = noise };

            var image = new Image();
            var transitionsRenderer = new ImageRenderer() { SourceNoiseMap = map, DestinationImage = image };
            transitionsRenderer.BuildTerrainGradient();
            builder.SetBounds(0, 1600, 0, 1200);
            builder.SetDestSize(1600, 1200);
            builder.Build();
            transitionsRenderer.Render();

            var bmp = transitionsRenderer.DestinationImage.ToGdiBitmap();
            bmp.Save("_river.bmp");

            Assert.Equal(0, 0);
        });
    }

    [Fact(DisplayName = "Humidity", Timeout = 10000)]
    public async void HumidityAsync()
    {
        await Task.Run(() =>
        {
            var noise = noiseGen.HumidityNoise;
            var map = new NoiseMap();
            PlaneNoiseMapBuilder builder =
                new PlaneNoiseMapBuilder() { DestNoiseMap = map, SourceModule = noise };

            var image = new Image();
            var transitionsRenderer = new ImageRenderer() { SourceNoiseMap = map, DestinationImage = image };
            transitionsRenderer.BuildGrayscaleGradient();
            builder.SetBounds(0, 1600, 0, 1200);
            builder.SetDestSize(1600, 1200);
            builder.Build();
            transitionsRenderer.Render();

            var bmp = transitionsRenderer.DestinationImage.ToGdiBitmap();
            bmp.Save("_humidity.bmp");

            Assert.Equal(0, 0);
        });
    }

    [Fact(DisplayName = "Erosion", Timeout = 10000)]
    public async void ErosionAsync()
    {
        await Task.Run(() =>
        {
            var noise = noiseGen.ErosionNoise;
            var map = new NoiseMap();
            PlaneNoiseMapBuilder builder =
                new PlaneNoiseMapBuilder() { DestNoiseMap = map, SourceModule = noise };

            var image = new Image();
            var transitionsRenderer = new ImageRenderer() { SourceNoiseMap = map, DestinationImage = image };
            transitionsRenderer.BuildGrayscaleGradient();
            builder.SetBounds(0, 1600, 0, 1200);
            builder.SetDestSize(1600, 1200);
            builder.Build();
            transitionsRenderer.Render();

            var bmp = transitionsRenderer.DestinationImage.ToGdiBitmap();
            bmp.Save("_erosion.bmp");

            Assert.Equal(0, 0);
        });
    }
}
