using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Nbt.Tags;
using Obsidian.Util.Collection;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.WorldData
{
    public class Region
    {
        public const int CUBIC_REGION_SIZE_SHIFT = 3;
        public const int CUBIC_REGION_SIZE = 1 << CUBIC_REGION_SIZE_SHIFT;

        private bool cancel = false;
        public int X { get; }
        public int Z { get; }

        public ConcurrentDictionary<int, Entity> Entities { get; private set; } = new ConcurrentDictionary<int, Entity>();

        public DenseCollection<Chunk> LoadedChunks { get; private set; } = new DenseCollection<Chunk>(CUBIC_REGION_SIZE, CUBIC_REGION_SIZE);

        internal Region(int x, int z)
        {
            this.X = x;
            this.Z = z;
        }

        internal async Task BeginTickAsync(CancellationToken cts)
        {
            while (!cts.IsCancellationRequested || cancel)
            {
                await Task.Delay(20);

                foreach (var (_, entity) in this.Entities)
                    await entity.TickAsync();
            }
        }

        internal void Cancel() => this.cancel = true;

        public Chunk LoadChunk(int relativeX, int relativeZ)
        {
            // See if chunk is already loaded
            if (LoadedChunks[relativeX, relativeZ] is Chunk c) { return c; }
            var chunk = new Chunk(
                relativeX << CUBIC_REGION_SIZE_SHIFT,
                relativeZ << CUBIC_REGION_SIZE_SHIFT
                );

            return null;
        }

        public void FlushChunks(string worldPath)
        {
            // Sanity check chunks folder exists
            var chunksFolder = Path.Join(worldPath, "regions", $"{X}.{Z}", "chunks");
            Directory.CreateDirectory(chunksFolder);
            foreach (var c in LoadedChunks)
            {
                var chunkPath = Path.Join(chunksFolder, $"{c.X}_{c.Z}.cnk");
                var chunkFile = new NbtFile();

                var sectionsCompound = new List<NbtCompound>();
                foreach(var section in c.Sections)
                {
                    if (section.YBase is null) { continue; }
 
                    var sec = new NbtCompound()
                    {
                        new NbtByte("Y", (byte)section.YBase),
                        new NbtList("Palette", new List<NbtTag>()),
                        new NbtLongArray("BlockStates", section.BlockStorage.Storage)
                    };
                    sectionsCompound.Add(sec);
                }

                var blocksCompound = new List<NbtCompound>();
                foreach (var block in c.Blocks)
                {
                    var b = new NbtCompound()
                    {
                        new NbtShort("index", block.Key),
                        new NbtDouble("X", block.Value.Location.X),
                        new NbtDouble("Y", block.Value.Location.Y),
                        new NbtDouble("Z", block.Value.Location.Z),
                        new NbtInt("id", block.Value.Id),
                        new NbtString("material", block.Value.Type.ToString()),
                    };
                    blocksCompound.Add(b);
                }

                var chunkCompound = new NbtCompound("Data")
                {
                    new NbtInt("xPos", c.X),
                    new NbtInt("zPos", c.Z),
                    new NbtIntArray("Biomes", c.BiomeContainer.Biomes.ToArray()),
                    new NbtCompound("Heightmaps")
                    {
                        new NbtLongArray("MOTION_BLOCKING", c.Heightmaps[HeightmapType.MotionBlocking].data.Storage),
                        new NbtLongArray("OCEAN_FLOOR", c.Heightmaps[HeightmapType.OceanFloor].data.Storage),
                        new NbtLongArray("WORLD_SURFACE", c.Heightmaps[HeightmapType.WorldSurface].data.Storage),
                    },
                    //new NbtList("Sections", sectionsCompound), // Do we even use sections?
                    new NbtList("Blocks", blocksCompound)
                };

                chunkFile.RootTag = chunkCompound;
                chunkFile.SaveToFile(chunkPath, NbtCompression.GZip);
            }
        }
    }
}
