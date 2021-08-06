using Obsidian.ChunkData;
using Obsidian.WorldData.Generators.Overworld.Terrain;
using static Obsidian.API.Noise.VoronoiBiomes;

namespace Obsidian.WorldData.Generators.Overworld
{
    public enum Temp
    {
        hot,
        warm,
        cold,
        freezing
    }

    public enum Humidity
    {
        wet = 0,
        neutral = 1,
        dry = 2
    }

    public static class ChunkBiome
    {
        public static Biomes GetBiome(int worldX, int worldZ, OverworldTerrain noiseGen)
        {
            BiomeNoiseValue bn = noiseGen.GetBiome(worldX, worldZ);

            switch (bn)
            {
                case BiomeNoiseValue.DeepOcean:
                    return Biomes.DeepOcean;
                case BiomeNoiseValue.DeepFrozenOcean:
                    return Biomes.DeepFrozenOcean;
                case BiomeNoiseValue.FrozenOcean:
                    return Biomes.FrozenOcean;
                case BiomeNoiseValue.DeepColdOcean:
                    return Biomes.DeepColdOcean;
                case BiomeNoiseValue.ColdOcean:
                    return Biomes.ColdOcean;
                case BiomeNoiseValue.DeepLukewarmOcean:
                    return Biomes.DeepLukewarmOcean;
                case BiomeNoiseValue.LukewarmOcean:
                    return Biomes.LukewarmOcean;
                case BiomeNoiseValue.DeepWarmOcean:
                    return Biomes.DeepWarmOcean;
                case BiomeNoiseValue.WarmOcean:
                    return Biomes.WarmOcean;
                case BiomeNoiseValue.Beach:
                    return Biomes.Beach;
                case BiomeNoiseValue.River:
                    return Biomes.River;
                case BiomeNoiseValue.Badlands:
                    return Biomes.Badlands;
                case BiomeNoiseValue.Desert:
                    return Biomes.Desert;
                case BiomeNoiseValue.Savanna:
                    return Biomes.Savanna;
                case BiomeNoiseValue.ShatteredSavanna:
                    return Biomes.ShatteredSavanna;
                case BiomeNoiseValue.SavannaPlateau:
                    return Biomes.SavannaPlateau;
                case BiomeNoiseValue.ShatteredSavannaPlateau:
                    return Biomes.ShatteredSavannaPlateau;
                case BiomeNoiseValue.Jungle:
                    return Biomes.Jungle;
                case BiomeNoiseValue.MushroomFields:
                    return Biomes.MushroomFields;
                case BiomeNoiseValue.Plains:
                    return Biomes.Plains;
                case BiomeNoiseValue.Swamp:
                    return Biomes.Swamp;
                case BiomeNoiseValue.DarkForest:
                    return Biomes.DarkForest;
                case BiomeNoiseValue.Forest:
                    return Biomes.Forest;
                case BiomeNoiseValue.TallBirchForest:
                    return Biomes.TallBirchForest;
                case BiomeNoiseValue.BirchForest:
                    return Biomes.BirchForest;
                case BiomeNoiseValue.GiantTreeTaiga:
                    return Biomes.GiantTreeTaiga;
                case BiomeNoiseValue.GiantSpruceTaiga:
                    return Biomes.GiantSpruceTaiga;
                case BiomeNoiseValue.Taiga:
                    return Biomes.Taiga;
                case BiomeNoiseValue.StoneShore:
                    return Biomes.StoneShore;
                case BiomeNoiseValue.ExtremeHills:
                    return Biomes.Mountains;
                case BiomeNoiseValue.SnowyBeach:
                    return Biomes.SnowyBeach;
                case BiomeNoiseValue.SnowyTundra:
                    return Biomes.SnowyTundra;
                case BiomeNoiseValue.SnowyTaiga:
                    return Biomes.SnowyTaiga;
                case BiomeNoiseValue.MountainGrove:
                    return Biomes.EndBarrens; //1.17 todo
                case BiomeNoiseValue.SnowySlopes:
                    return Biomes.EndBarrens; // 1.17 todo
                case BiomeNoiseValue.MountainMeadow:
                    return Biomes.EndBarrens; //1.17 todo
                case BiomeNoiseValue.LoftyPeaks:
                    return Biomes.EndBarrens; // 1.17 todo
                case BiomeNoiseValue.SnowCappedPeaks:
                    return Biomes.EndBarrens; // 1.17 todo

                default:
                    return Biomes.Nether; // Return something obvious
            }
        }
    }
}
