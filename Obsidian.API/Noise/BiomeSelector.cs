using SharpNoise.Modules;

namespace Obsidian.API.Noise;

public class BiomeSelector : Module
{
    // 3D: 5 heights, 4 temp, 3 humidity
    private readonly Biome[,,] BiomeLookup = new Biome[5, 4, 3] {
        {
            { Biome.DeepFrozenOcean, Biome.DeepFrozenOcean, Biome.DeepFrozenOcean }, // deep ocean, frozen, low-med-high humidity
            { Biome.DeepColdOcean, Biome.DeepColdOcean, Biome.DeepColdOcean }, // deep ocean, cold, low-med-high humidity
            { Biome.DeepLukewarmOcean, Biome.DeepLukewarmOcean, Biome.DeepLukewarmOcean }, // deep ocean, warm, low-med-high humidity
            { Biome.DeepOcean, Biome.DeepOcean, Biome.DeepOcean }, // deep ocean, hot, low-med-high humidity
        },
        {
            { Biome.FrozenOcean, Biome.FrozenOcean, Biome.FrozenOcean }, //  ocean, frozen, low-med-high humidity
            { Biome.ColdOcean, Biome.ColdOcean, Biome.ColdOcean }, //  ocean, cold, low-med-high humidity
            { Biome.LukewarmOcean, Biome.LukewarmOcean, Biome.LukewarmOcean }, //  ocean, warm, low-med-high humidity
            { Biome.Ocean, Biome.Ocean, Biome.Ocean }, //  ocean, hot, low-med-high humidity
        },
        {
            { Biome.IceSpikes, Biome.SnowyPlains, Biome.SnowyTaiga }, // flatland, frozen, low-med-high humidity
            { Biome.Plains, Biome.Plains, Biome.Taiga }, // flatland, cold, low-med-high humidity
            { Biome.Forest, Biome.Forest, Biome.BambooJungle}, // flatland, warm, low-med-high humidity
            { Biome.Savanna, Biome.Desert, Biome.Swamp }, // flatland, hot, low-med-high humidity
        },
        {
            { Biome.WindsweptForest, Biome.SnowySlopes, Biome.SnowySlopes }, // hills, frozen, low-med-high humidity
            { Biome.WindsweptHills, Biome.Grove, Biome.Forest }, // hills, cold, low-med-high humidity
            { Biome.WindsweptGravellyHills, Biome.SunflowerPlains, Biome.Jungle }, // hills, warm, low-med-high humidity
            { Biome.SavannaPlateau, Biome.Badlands, Biome.MangroveSwamp }, // hills, hot, low-med-high humidity
        },
        {
            { Biome.FrozenPeaks, Biome.FrozenPeaks, Biome.IceSpikes }, // mountains, frozen, low-med-high humidity
            { Biome.FrozenPeaks, Biome.SnowySlopes, Biome.SnowySlopes }, // mountains, cold, low-med-high humidity
            { Biome.StonyPeaks, Biome.StonyPeaks, Biome.JaggedPeaks }, // mountains, warm, low-med-high humidity
            { Biome.WindsweptSavanna, Biome.JaggedPeaks, Biome.JaggedPeaks }, // mountains, hot, low-med-high humidity
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
        var erosionVal = SourceModules[3].GetValue(x, 0, z) + 2.0;

        var height = SourceModules[2].GetValue(x, 0, z);
        if (height >= -0.01)
        {
            // Check river
            var riverVal = SourceModules[4].GetValue(x, 0, z);
            if (riverVal < 0.04)
            {
                return (double)Biome.River;
            }
        }
        if (height >= -0.1 && height < 0.04) { return (double)Biome.Beach; }
        if (height > 0.1) // If above ocean, add erosion and rivers
        {
            erosionVal = (height - 0.1) * erosionVal;
            height += erosionVal;
        }
        if (height >= 0.6) // Add mountain peaks/valleys
        {
            var peakVal = (height - 0.6) * Math.Max(SourceModules[5].GetValue(x, 0, z) + 1.6, 1.0) * 0.5;
            height += peakVal;// * (erosionVal - 0.5);
        }

        var heightIndex = height switch
        {
            double v when v < -0.6 => 0,
            double v when v >= -0.6 && v < 0 => 1,
            double v when v >= 0 && v < 0.3 => 2,
            double v when v >= 0.3 && v < 1 => 3,
            _ => 4,
        };

        //var heightIndex = (int)((heightVal + 1d) * 2.5d);
        return (double)BiomeLookup[heightIndex, tempIndex, humidityIndex];
    }
}
