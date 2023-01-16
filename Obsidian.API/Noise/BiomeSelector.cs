using SharpNoise.Modules;

namespace Obsidian.API.Noise;

public class BiomeSelector : Module
{
    // 3D: 5 heights, 4 temp, 3 humidity
    private readonly Biomes[,,] BiomeLookup = new Biomes[5, 4, 3] {
        {
            { Biomes.DeepFrozenOcean, Biomes.DeepFrozenOcean, Biomes.DeepFrozenOcean }, // deep ocean, frozen, low-med-high humidity
            { Biomes.DeepColdOcean, Biomes.DeepColdOcean, Biomes.DeepColdOcean }, // deep ocean, cold, low-med-high humidity
            { Biomes.DeepLukewarmOcean, Biomes.DeepLukewarmOcean, Biomes.DeepLukewarmOcean }, // deep ocean, warm, low-med-high humidity
            { Biomes.DeepOcean, Biomes.DeepOcean, Biomes.DeepOcean }, // deep ocean, hot, low-med-high humidity
        },
        {
            { Biomes.FrozenOcean, Biomes.FrozenOcean, Biomes.FrozenOcean }, //  ocean, frozen, low-med-high humidity
            { Biomes.ColdOcean, Biomes.ColdOcean, Biomes.ColdOcean }, //  ocean, cold, low-med-high humidity
            { Biomes.LukewarmOcean, Biomes.LukewarmOcean, Biomes.LukewarmOcean }, //  ocean, warm, low-med-high humidity
            { Biomes.Ocean, Biomes.Ocean, Biomes.Ocean }, //  ocean, hot, low-med-high humidity
        },
        {
            { Biomes.IceSpikes, Biomes.SnowyPlains, Biomes.SnowyTaiga }, // flatland, frozen, low-med-high humidity
            { Biomes.Plains, Biomes.Plains, Biomes.Taiga }, // flatland, cold, low-med-high humidity
            { Biomes.Forest, Biomes.Forest, Biomes.WindsweptSavanna }, // flatland, warm, low-med-high humidity
            { Biomes.Savanna, Biomes.Desert, Biomes.Swamp }, // flatland, hot, low-med-high humidity
        },
        {
            { Biomes.WindsweptForest, Biomes.SnowySlopes, Biomes.SnowySlopes }, // hills, frozen, low-med-high humidity
            { Biomes.WindsweptHills, Biomes.Grove, Biomes.Forest }, // hills, cold, low-med-high humidity
            { Biomes.WindsweptGravellyHills, Biomes.SunflowerPlains, Biomes.Jungle }, // hills, warm, low-med-high humidity
            { Biomes.SavannaPlateau, Biomes.Badlands, Biomes.MangroveSwamp }, // hills, hot, low-med-high humidity
        },
        {
            { Biomes.FrozenPeaks, Biomes.FrozenPeaks, Biomes.IceSpikes }, // mountains, frozen, low-med-high humidity
            { Biomes.FrozenPeaks, Biomes.SnowySlopes, Biomes.SnowySlopes }, // mountains, cold, low-med-high humidity
            { Biomes.StonyPeaks, Biomes.StonyPeaks, Biomes.JaggedPeaks }, // mountains, warm, low-med-high humidity
            { Biomes.StonyPeaks, Biomes.JaggedPeaks, Biomes.JaggedPeaks }, // mountains, hot, low-med-high humidity
        }
    };

    public BiomeSelector(Module temp, Module humidity, Module height) : base(3)
    {
        SourceModules[0] = temp;
        SourceModules[1] = humidity;
        SourceModules[2] = height;
    }

    public override double GetValue(double x, double y, double z)
    {
        // 5 heights, 4 temps, 3 humidities
        var tempIndex = (int)((SourceModules[0].GetValue(x, 0, z) + 0.999d) * 2.0d);
        var humidityIndex = (int)((SourceModules[1].GetValue(x, 0, z) + 0.999d) * 1.5d);
        var heightIndex = (int)((SourceModules[2].GetValue(x, 0, z) + 0.999d) * 2.5d);
        return (double) BiomeLookup[heightIndex, tempIndex, humidityIndex];
    }
}
