using Obsidian.API;
using Obsidian.Util.Registry;

namespace Obsidian.WorldData.Generators
{
    public class SuperflatGenerator : WorldGenerator
    {
        public SuperflatGenerator() : base("superflat") { }

        public override Chunk GenerateChunk(int x, int z)
        {
            var chunk = new Chunk(x, z);
            for (var x1 = 0; x1 < 16; x1++)
            {
                for (var z1 = 0; z1 < 16; z1++)
                {
                    chunk.SetBlock(x1, 5, z1, Registry.GetBlock(Materials.GrassBlock));
                    chunk.SetBlock(x1, 4, z1, Registry.GetBlock(Materials.Dirt));
                    chunk.SetBlock(x1, 3, z1, Registry.GetBlock(Materials.Dirt));
                    chunk.SetBlock(x1, 2, z1, Registry.GetBlock(Materials.Dirt));
                    chunk.SetBlock(x1, 1, z1, Registry.GetBlock(Materials.Dirt));
                    chunk.SetBlock(x1, 0, z1, Registry.GetBlock(Materials.Bedrock));
                }
            }

            return chunk;
        }
    }
}