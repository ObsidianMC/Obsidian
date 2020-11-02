using Obsidian.ChunkData;
using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.WorldData.Generators.Overworld
{
    public static class ChunkBiome
    {
        public static Biomes GetBiome(int worldX, int worldZ, OverworldNoise noiseGen)
        {
            Biomes b = Biomes.Nether;
            bool isHot = noiseGen.GetBiomeTemp(worldX, 0, worldZ) < 0;
            bool isHumid = noiseGen.GetBiomeHumidity(worldX, 0, worldZ) < 0;

            // River
            if (noiseGen.isRiver(worldX, worldZ))
            {
                if (isHot)
                    b = Biomes.River;
                else
                    b = Biomes.FrozenRiver;
            }
            // Mountain
            else if (noiseGen.isMountain(worldX, worldZ))
            {
                if (isHot)
                {
                    if (isHumid)
                        b = Biomes.WoodedMountains;
                    else
                        b = Biomes.Mountains;
                }
                else // Cold Mountain
                {
                    if (isHumid)
                        b = Biomes.SnowyTaigaMountains;
                    else
                        b = Biomes.SnowyMountains;
                }
            }
            // Badlands/Foothills
            else if (noiseGen.isBadlands(worldX, worldZ))
            {
                if (isHot)
                {
                    if (isHumid)
                        b = Biomes.SwampHills;
                    else
                        b = Biomes.WoodedBadlandsPlateau;
                }
                else // Cold Badlands
                {
                    if (isHumid)
                        b = Biomes.SnowyTundra;
                    else
                        b = Biomes.Badlands;
                }
            }
            // Hills
            else if (noiseGen.isHills(worldX, worldZ))
            {
                if (isHot)
                {
                    if (isHumid)
                        b = Biomes.JungleHills;
                    else
                        b = Biomes.DesertHills;
                }
                else // Cold Hills
                {
                    if (isHumid)
                        b = Biomes.BirchForestHills;
                    else
                        b = Biomes.DarkForestHills;
                }
            }
            //Plains
            else if (noiseGen.isPlains(worldX, worldZ))
            {
                if (isHot)
                {
                    if (isHumid)
                        b = Biomes.Savanna;
                    else
                        b = Biomes.Desert;
                } 
                else // Cold Plains
                {
                    if (isHumid)
                        b = Biomes.SnowyTaiga;
                    else
                        b = Biomes.Plains;
                }
            }
            // Ocean
            else if (noiseGen.isOcean(worldX, worldZ))
            {
                bool isDeep = noiseGen.isDeepOcean(worldX, worldZ);
                if (isHot) // Hot Ocean
                {
                    if (isHumid)
                    {
                        if (isDeep)
                            b = Biomes.DeepWarmOcean;
                        else
                            b = Biomes.WarmOcean;
                    }
                    else
                    {
                        if (isDeep)
                            b = Biomes.DeepLukewarmOcean;
                        else
                            b = Biomes.LukewarmOcean;
                    }
                }
                else // Cold Ocean
                {
                    if (isHumid)
                    {
                        if (isDeep)
                            b = Biomes.DeepColdOcean;
                        else
                            b = Biomes.ColdOcean;
                    }
                    else
                    {
                        if (isDeep)
                            b = Biomes.DeepFrozenOcean;
                        else
                            b = Biomes.FrozenOcean;
                    }
                }
            }
            else { b = Biomes.Plains; }
            return b;
        }
    }
}
