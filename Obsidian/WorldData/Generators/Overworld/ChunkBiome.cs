using Obsidian.ChunkData;
using Obsidian.WorldData.Generators.Overworld.Terrain;

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
            Temp t;
            double temperature = noiseGen.GetBiomeTemp(worldX, worldZ);
            if (temperature > 0.66) { t = Temp.hot; }
            else if (temperature > 0.0) { t = Temp.warm; }
            else if (temperature > -0.5) { t = Temp.cold; }
            else { t = Temp.freezing; }

            Humidity h;
            double humidity = noiseGen.GetBiomeHumidity(worldX, worldZ);
            if (humidity > 0.66) { h = Humidity.dry; }
            else if (humidity > 0) { h = Humidity.neutral; }
            else { h = Humidity.wet; }

            Biomes b = Biomes.Nether;
            // River
            if (noiseGen.IsRiver(worldX, worldZ))
            {
                b = t switch
                {
                    Temp.hot => Biomes.River,
                    Temp.warm => Biomes.River,
                    Temp.cold => Biomes.River,
                    Temp.freezing => Biomes.FrozenRiver,
                    _ => throw new System.NotImplementedException()
                };
            }
            // Ocean
            else if (noiseGen.IsOcean(worldX, worldZ))
            {
                switch (t)
                {
                    case Temp.hot:
                        b = Biomes.WarmOcean;
                        break;
                    case Temp.warm:
                        b = Biomes.LukewarmOcean;
                        break;
                    case Temp.cold:
                        b = Biomes.ColdOcean;
                        break;
                    case Temp.freezing:
                        b = Biomes.FrozenOcean;
                        break;
                }
            }
            // Mountain
            else if (noiseGen.IsMountain(worldX, worldZ))
            {
                switch (t)
                {
                    case Temp.hot:
                        b = h switch
                        {
                            Humidity.wet => Biomes.DesertHills,
                            Humidity.neutral => Biomes.Mountains,
                            Humidity.dry => Biomes.ErodedBadlands,
                            _ => throw new System.NotImplementedException()
                        }; break;
                    case Temp.warm:
                        b = h switch
                        {
                            Humidity.wet => Biomes.TallBirchHills,
                            Humidity.neutral => Biomes.Mountains,
                            Humidity.dry => Biomes.WoodedBadlandsPlateau,
                            _ => throw new System.NotImplementedException()
                        }; break;
                    case Temp.cold:
                        b = h switch
                        {
                            Humidity.wet => Biomes.SnowyTaigaMountains,
                            Humidity.neutral => Biomes.WoodedMountains,
                            Humidity.dry => Biomes.Mountains,
                            _ => throw new System.NotImplementedException()
                        }; break;
                    case Temp.freezing:
                        b = h switch
                        {
                            Humidity.wet => Biomes.SnowyTaigaMountains,
                            Humidity.neutral => Biomes.SnowyMountains,
                            Humidity.dry => Biomes.GravellyMountains,
                            _ => throw new System.NotImplementedException()
                        }; break;
                }
            }
            // Hills
            else if (noiseGen.IsHills(worldX, worldZ))
            {
                switch (t)
                {
                    case Temp.hot:
                        b = h switch
                        {
                            Humidity.wet => Biomes.Jungle,
                            Humidity.neutral => Biomes.BadlandsPlateau,
                            Humidity.dry => Biomes.DesertHills,
                            _ => throw new System.NotImplementedException()
                        }; break;
                    case Temp.warm:
                        b = h switch
                        {
                            Humidity.wet => Biomes.TallBirchForest,
                            Humidity.neutral => Biomes.DarkForest,
                            Humidity.dry => Biomes.GiantSpruceTaiga,
                            _ => throw new System.NotImplementedException()
                        }; break;
                    case Temp.cold:
                        b = h switch
                        {
                            Humidity.wet => Biomes.GiantSpruceTaiga,
                            Humidity.neutral => Biomes.FlowerForest,
                            Humidity.dry => Biomes.Forest,
                            _ => throw new System.NotImplementedException()
                        }; break;
                    case Temp.freezing:
                        b = h switch
                        {
                            Humidity.wet => Biomes.SnowyTaigaMountains,
                            Humidity.neutral => Biomes.SnowyMountains,
                            Humidity.dry => Biomes.GravellyMountains,
                            _ => throw new System.NotImplementedException()
                        }; break;
                }
            }
            //Plains
            else if (noiseGen.IsPlains(worldX, worldZ))
            {
                switch (t)
                {
                    case Temp.hot:
                        b = h switch
                        {
                            Humidity.wet => Biomes.Swamp,
                            Humidity.neutral => Biomes.Badlands,
                            Humidity.dry => Biomes.Desert,
                            _ => throw new System.NotImplementedException()
                        }; break;
                    case Temp.warm:
                        b = h switch
                        {
                            Humidity.wet => Biomes.BirchForest,
                            Humidity.neutral => Biomes.Plains,
                            Humidity.dry => Biomes.Savanna,
                            _ => throw new System.NotImplementedException()
                        }; break;
                    case Temp.cold:
                        b = h switch
                        {
                            Humidity.wet => Biomes.GiantSpruceTaiga,
                            Humidity.neutral => Biomes.SnowyTaiga,
                            Humidity.dry => Biomes.Plains,
                            _ => throw new System.NotImplementedException()
                        }; break;
                    case Temp.freezing:
                        b = h switch
                        {
                            Humidity.wet => Biomes.IceSpikes,
                            Humidity.neutral => Biomes.SnowyTundra,
                            Humidity.dry => Biomes.SnowyBeach,
                            _ => throw new System.NotImplementedException()
                        }; break;
                }
            }
            
            else
            {
                b = Biomes.Plains;
            }
            return b;
        }
    }
}
