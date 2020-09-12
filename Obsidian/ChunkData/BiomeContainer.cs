using Obsidian.Net;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public class BiomeContainer
    {
        public List<int> Biomes { get; set; } = new List<int>(1024);

        public BiomeContainer() { }

        public async Task WriteToAsync(MinecraftStream stream)
        {
            foreach (var biome in this.Biomes)
                await stream.WriteIntAsync(biome);
        }
    }

}
