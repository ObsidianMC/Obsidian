﻿using Obsidian.API;
using Obsidian.WorldData.Generators.Overworld;
using SharpNoise;
using SharpNoise.Builders;
using SharpNoise.Utilities.Imaging;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public class Noise
{
    private OverworldTerrainNoise noiseGen = new OverworldTerrainNoise(123456, true);

    [Fact(DisplayName = "Biomes", Timeout = 100000)]
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


            builder.SetBounds(-400, 400, -300, 300);
            builder.SetDestSize(800, 600);
            builder.Build();
            biomesRenderer.Render();

            var bmp = biomesRenderer.DestinationImage.ToGdiBitmap();
            bmp.Save("_biomes.bmp");

            Assert.Equal(0, 0);
        });
    }

    [Fact(DisplayName = "Transitions", Timeout = 100000)]
    public async void TransitionsAsync()
    {
        await Task.Run(() =>
        {
            var map = new NoiseMap();
            PlaneNoiseMapBuilder builder =
                new PlaneNoiseMapBuilder() { DestNoiseMap = map, SourceModule = noiseGen.transitions };

            var image = new Image();
            var transitionsRenderer = new ImageRenderer() { SourceNoiseMap = map, DestinationImage = image };
            transitionsRenderer.BuildGrayscaleGradient();
            builder.SetBounds(-400, 400, -300, 300);
            builder.SetDestSize(800, 600);
            builder.Build();
            transitionsRenderer.Render();

            var bmp = transitionsRenderer.DestinationImage.ToGdiBitmap();
            bmp.Save("_transitions.bmp");

            Assert.Equal(0, 0);
        });
    }

    [Fact(DisplayName = "Terrain", Timeout = 100000)]
    public async void TerrainAsync()
    {
        await Task.Run(() =>
        {
            var noise = new SharpNoise.Modules.ScaleBias()
            {
                Scale = 4,
                Source0 = noiseGen.biomeTerrain
            };

            var map = new NoiseMap();
            PlaneNoiseMapBuilder builder =
                new PlaneNoiseMapBuilder() { DestNoiseMap = map, SourceModule = noise };

            var image = new Image();
            var transitionsRenderer = new ImageRenderer() { SourceNoiseMap = map, DestinationImage = image };
            transitionsRenderer.BuildTerrainGradient();
            builder.SetBounds(-400, 400, -300, 300);
            builder.SetDestSize(800, 600);
            builder.Build();
            transitionsRenderer.Render();

            var bmp = transitionsRenderer.DestinationImage.ToGdiBitmap();
            bmp.Save("_terrain.bmp");

            Assert.Equal(0, 0);
        });
    }

    [Fact(DisplayName = "Terrain Blending", Timeout = 1000000)]
    public async void TerrainBlendAsync()
    {
        await Task.Run(() =>
        {
            var noise = new SharpNoise.Modules.ScaleBias()
            {
                Scale = 4,
                Source0 = noiseGen.selectiveBlend
            };

            var map = new NoiseMap();
            PlaneNoiseMapBuilder builder =
                new PlaneNoiseMapBuilder() { DestNoiseMap = map, SourceModule = noise };

            var image = new Image();
            var transitionsRenderer = new ImageRenderer() { SourceNoiseMap = map, DestinationImage = image };
            transitionsRenderer.BuildTerrainGradient();
            builder.SetBounds(-400, 400, -300, 300);
            builder.SetDestSize(800, 600);
            builder.Build();
            transitionsRenderer.Render();

            var bmp = transitionsRenderer.DestinationImage.ToGdiBitmap();
            bmp.Save("_blendedterrain.bmp");

            Assert.Equal(0, 0);
        });
    }
}
