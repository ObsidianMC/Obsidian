using Obsidian.Net;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public class BiomeContainer
    {
        public List<int> Biomes { get; set; } = new List<int>(1024);

        public BiomeContainer()
        {
            for (int x = 0; x < 1024; x++)
                Biomes.Add(0);
        }

        public void SetBiome(int bx, int by, int bz, Biomes biome)
        {
            Biomes[GetIndex(bx, by, bz)] = (int)biome;
        }

        public Biomes GetBiome(int bx, int by, int bz)
        {
            return (Biomes)Biomes[GetIndex(bx, by, bz)];
        }

        private int GetIndex(int x, int y, int z) => ((y >> 2) & 63) << 4 | ((z >> 2) & 3) << 2 | ((x >> 2) & 3);

        public async Task WriteToAsync(MinecraftStream stream)
        {
            await stream.WriteVarIntAsync(1024);

            foreach (var biome in this.Biomes)
                await stream.WriteVarIntAsync(biome);
        }

        public void WriteTo(MinecraftStream stream)
        {
            stream.WriteVarInt(1024);

            foreach (var biome in Biomes)
                stream.WriteVarInt(biome);
        }
    }
}
