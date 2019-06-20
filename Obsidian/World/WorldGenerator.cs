using System.Collections.Generic;

namespace Obsidian.World
{
    public abstract class WorldGenerator
    {
        public List<Chunk> Chunks { get; }

        public WorldGenerator()
        {
            this.Chunks = new List<Chunk>();
        }
        
        public abstract void GenerateChunk(Chunk chunk);
    }
}
