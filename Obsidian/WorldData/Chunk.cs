using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Nbt.Tags;
using Obsidian.API;
using System.Collections.Generic;

namespace Obsidian.WorldData
{
    public class Chunk
    {
        public int X { get; }
        public int Z { get; }

        public BiomeContainer BiomeContainer { get; private set; } = new BiomeContainer();

        private SebastiansChunk SebastiansChunk { get; } = new SebastiansChunk();

        public ChunkSection[] Sections { get; private set; } = new ChunkSection[16];
        public List<NbtTag> BlockEntities { get; private set; } = new List<NbtTag>();

        public Dictionary<HeightmapType, Heightmap> Heightmaps { get; private set; } = new Dictionary<HeightmapType, Heightmap>();

        public Chunk(int x, int z)
        {
            X = x;
            Z = z;

            Heightmaps.Add(HeightmapType.MotionBlocking, new Heightmap(HeightmapType.MotionBlocking, this));

            Init();
        }

        private void Init()
        {
            for (int i = 0; i < 16; i++)
                this.Sections[i] = new ChunkSection();
        }

        public Block GetBlock(Position position) => this.GetBlock((int)position.X, (int)position.Y, (int)position.Z);

        public Block GetBlock(int x, int y, int z)
        {
            return SebastiansChunk.GetBlock(x, y, z);
        }

        public SebastiansBlock GetLightBlock(Position position) => GetLightBlock((int)position.X, (int)position.Y, (int)position.Z);

        public SebastiansBlock GetLightBlock(int x, int y, int z)
        {
            return SebastiansChunk.GetLightBlock(x, y, z);
        }

        public void SetBlock(Position position, Block block) => this.SetBlock((int)position.X, (int)position.Y, (int)position.Z, block);

        public void SetBlock(int x, int y, int z, Block block)
        {
            SebastiansChunk.SetBlock(x, y, z, block);

            this.Sections[y >> 4].SetBlock(x, y & 15, z, block);
        }

        public void SetBlock(Position position, SebastiansBlock block) => SetBlock((int)position.X, (int)position.Y, (int)position.Z, block);

        public void SetBlock(int x, int y, int z, SebastiansBlock block)
        {
            SebastiansChunk.SetBlock(x, y, z, block);
            Sections[y >> 4].SetBlock(x, y & 15, z, block);
        }

        public void CalculateHeightmap()
        {
            SebastiansChunk.CalculateHeightmap(target: Heightmaps[HeightmapType.MotionBlocking]);
        }

        public void CheckHomogeneity()
        {
            SebastiansChunk.CheckHomogeneity();
        }
    }
}