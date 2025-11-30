using Obsidian.API.Registries;
using Obsidian.API.Registry.Codecs.Biomes;
using SharpNoise.Modules;

namespace Obsidian.API.Noise;

public class BiomeSelector : Module
{
    // 3D: 5 heights, 4 temp, 3 humidity
    private static readonly BiomeCodec[,,] BiomeLookup = new BiomeCodec[5, 4, 3] {
        {
            { CodecRegistry.Biomes.DeepFrozenOcean, CodecRegistry.Biomes.DeepFrozenOcean, CodecRegistry.Biomes.DeepFrozenOcean }, // deep ocean, frozen, low-med-high humidity
            { CodecRegistry.Biomes.DeepColdOcean, CodecRegistry.Biomes.DeepColdOcean, CodecRegistry.Biomes.DeepColdOcean }, // deep ocean, cold, low-med-high humidity
            { CodecRegistry.Biomes.DeepLukewarmOcean, CodecRegistry.Biomes.DeepLukewarmOcean, CodecRegistry.Biomes.DeepLukewarmOcean }, // deep ocean, warm, low-med-high humidity
            { CodecRegistry.Biomes.DeepOcean, CodecRegistry.Biomes.DeepOcean, CodecRegistry.Biomes.DeepOcean }, // deep ocean, hot, low-med-high humidity
        },
        {
            { CodecRegistry.Biomes.FrozenOcean, CodecRegistry.Biomes.FrozenOcean, CodecRegistry.Biomes.FrozenOcean }, //  ocean, frozen, low-med-high humidity
            { CodecRegistry.Biomes.ColdOcean, CodecRegistry.Biomes.ColdOcean, CodecRegistry.Biomes.ColdOcean }, //  ocean, cold, low-med-high humidity
            { CodecRegistry.Biomes.LukewarmOcean, CodecRegistry.Biomes.LukewarmOcean, CodecRegistry.Biomes.LukewarmOcean }, //  ocean, warm, low-med-high humidity
            { CodecRegistry.Biomes.Ocean, CodecRegistry.Biomes.Ocean, CodecRegistry.Biomes.Ocean }, //  ocean, hot, low-med-high humidity
        },
        {
            { CodecRegistry.Biomes.IceSpikes, CodecRegistry.Biomes.SnowyPlains, CodecRegistry.Biomes.SnowyTaiga }, // flatland, frozen, low-med-high humidity
            { CodecRegistry.Biomes.Meadow, CodecRegistry.Biomes.Plains, CodecRegistry.Biomes.Taiga }, // flatland, cold, low-med-high humidity
            { CodecRegistry.Biomes.BirchForest, CodecRegistry.Biomes.Forest, CodecRegistry.Biomes.BambooJungle}, // flatland, warm, low-med-high humidity
            { CodecRegistry.Biomes.Savanna, CodecRegistry.Biomes.Desert, CodecRegistry.Biomes.Swamp }, // flatland, hot, low-med-high humidity
        },
        {
            { CodecRegistry.Biomes.WindsweptForest, CodecRegistry.Biomes.SnowySlopes, CodecRegistry.Biomes.SnowySlopes }, // hills, frozen, low-med-high humidity
            { CodecRegistry.Biomes.WindsweptHills, CodecRegistry.Biomes.Grove, CodecRegistry.Biomes.DarkForest }, // hills, cold, low-med-high humidity
            { CodecRegistry.Biomes.WindsweptGravellyHills, CodecRegistry.Biomes.SunflowerPlains, CodecRegistry.Biomes.Jungle }, // hills, warm, low-med-high humidity
            { CodecRegistry.Biomes.SavannaPlateau, CodecRegistry.Biomes.Badlands, CodecRegistry.Biomes.MangroveSwamp }, // hills, hot, low-med-high humidity
        },
        {
            { CodecRegistry.Biomes.FrozenPeaks, CodecRegistry.Biomes.FrozenPeaks, CodecRegistry.Biomes.IceSpikes }, // mountains, frozen, low-med-high humidity
            { CodecRegistry.Biomes.FrozenPeaks, CodecRegistry.Biomes.SnowySlopes, CodecRegistry.Biomes.SnowySlopes }, // mountains, cold, low-med-high humidity
            { CodecRegistry.Biomes.StonyPeaks, CodecRegistry.Biomes.StonyPeaks, CodecRegistry.Biomes.JaggedPeaks }, // mountains, warm, low-med-high humidity
            { CodecRegistry.Biomes.WindsweptSavanna, CodecRegistry.Biomes.JaggedPeaks, CodecRegistry.Biomes.JaggedPeaks }, // mountains, hot, low-med-high humidity
        }
    };

    public BiomeSelector(Module temp, Module humidity, Module height, Module erosion, Module river, Module peaks) : base(6)
    {
        SourceModules[0] = temp;
        SourceModules[1] = humidity;
        SourceModules[2] = height;
        SourceModules[3] = erosion;
        SourceModules[4] = river;
        SourceModules[5] = peaks;
    }

    public override double GetValue(double x, double y, double z)
    {
        // 5 heights, 4 temps, 3 humidities
        var tempIndex = (int)((SourceModules[0].GetValue(x, 0, z) + 0.999d) * 2.0d);
        var humidityIndex = (int)((SourceModules[1].GetValue(x, 0, z) + 0.999d) * 1.5d);
        var erosionVal = SourceModules[3].GetValue(x, 0, z) + 1.0;

        var height = SourceModules[2].GetValue(x, 0, z);
        if (height >= -0.01)
        {
            // Check river
            var riverVal = SourceModules[4].GetValue(x, 0, z);
            if (riverVal < 0.04)
                return tempIndex < 1 ? CodecRegistry.Biomes.FrozenRiver.Id : CodecRegistry.Biomes.River.Id;
        }
        if (height >= -0.1 && height < 0.04)
            return tempIndex <= 1 ? CodecRegistry.Biomes.SnowyBeach.Id : CodecRegistry.Biomes.Beach.Id;

        if (height > 0.1) // If above ocean, add erosion and rivers
        {
            erosionVal = (height - 0.1) * (erosionVal + 1.3);
            height += erosionVal;
        }
        if (height >= 0.6) // Add mountain peaks/valleys
        {
            var peakVal = (height - 0.6) * Math.Max(SourceModules[5].GetValue(x, 0, z) + 1.6, 1.0) * 0.5;
            height += peakVal * (erosionVal + 0.5);
        }

        var heightIndex = height switch
        {
            double v when v < -0.6 => 0,
            double v when v >= -0.6 && v < 0 => 1,
            double v when v >= 0 && v < 0.3 => 2,
            double v when v >= 0.3 && v < 1.5 => 3,
            _ => 4,
        };

        //var heightIndex = (int)((heightVal + 1d) * 2.5d);
        return BiomeLookup[heightIndex, tempIndex, humidityIndex].Id;
    }
}
