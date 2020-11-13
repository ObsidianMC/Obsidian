using Obsidian.API;
using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Nbt.Tags;
using Obsidian.Util;
using Obsidian.Util.Collection;
using Obsidian.Util.Registry;
using System;
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

        public string RegionFolder { get; }

        public ConcurrentDictionary<int, Entity> Entities { get; private set; } = new ConcurrentDictionary<int, Entity>();

        public DenseCollection<Chunk> LoadedChunks { get; private set; } = new DenseCollection<Chunk>(CUBIC_REGION_SIZE, CUBIC_REGION_SIZE);

        internal Region(int x, int z, string worldRegionsPath)
        {
            this.X = x;
            this.Z = z;
            RegionFolder = Path.Join(worldRegionsPath, $"{X}.{Z}");
            // Sanity check folders are created
            Directory.CreateDirectory(Path.Join(RegionFolder, "chunks"));
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

        public Chunk LoadChunk(int x, int z)
        {
            var relativeX = Helpers.Modulo(x, CUBIC_REGION_SIZE);
            var relativeZ = Helpers.Modulo(z, CUBIC_REGION_SIZE);
            // See if chunk is already loaded
            if (LoadedChunks[relativeX, relativeZ] is Chunk c) { return c; }
            // See if chunk is on disk
            var chunkFile = Path.Join(RegionFolder, "chunks", $"{x}_{z}.cnk");
            if (!File.Exists(chunkFile)) { return null; }

            var chunkNbt = new NbtFile();
            chunkNbt.LoadFromFile(chunkFile);
            var chunkCompound = chunkNbt.RootTag;
            var chunk = new Chunk(x, z);

            foreach (var bc in chunkCompound["Blocks"] as NbtList)
            {
                var index = bc["index"].ShortValue;
                var bx = bc["X"].DoubleValue;
                var by = bc["Y"].DoubleValue;
                var bz = bc["Z"].DoubleValue;
                var id = bc["id"].IntValue;
                var mat = bc["material"].StringValue;

                Block block = Registry.GetBlock((Materials) Enum.Parse(typeof(Materials), mat));
                block.Location = new Position(bx, by, bz);
                chunk.Blocks.Add(index, block);
            }

            foreach (var secCompound in chunkCompound["Sections"] as NbtList)
            {
                var secY = (int) secCompound["Y"].ByteValue;
                var states = (secCompound["BlockStates"] as NbtLongArray).Value;
                var palettes = secCompound["Palette"] as NbtList;

                chunk.Sections[secY].BlockStorage.Storage = states;

                var chunkSecPalette = (LinearBlockStatePalette)chunk.Sections[secY].Palette;
                var index = 0;
                foreach (var pallete in palettes)
                {

                    chunkSecPalette.BlockStateArray.SetValue(Registry.GetBlock(pallete["Name"].StringValue), index);
                    index++;
                }
                // chunkSecPalette.BlockStateCount = palettes.Count; // doesn't work ???? ¯\_(ツ)_/¯
                chunkSecPalette.BlockStateCount = chunkSecPalette.BlockStateArray.Where(a => a is not null).Count();
            }

            chunk.BiomeContainer.Biomes = (chunkCompound["Biomes"] as NbtIntArray).Value.ToList();

            foreach (var heightmap in chunkCompound["Heightmaps"] as NbtCompound)
            {
                var heightmapType = (HeightmapType) Enum.Parse(typeof(HeightmapType), heightmap.Name.Replace("_", ""), true);
                var values = ((NbtLongArray)heightmap).Value;
                chunk.Heightmaps[heightmapType].data.Storage = values;
            }

            return chunk;
        }

        public void FlushChunk(Chunk c)
        {
            var chunkPath = Path.Join(RegionFolder, "chunks", $"{c.X}_{c.Z}.cnk");
            var chunkFile = new NbtFile();

            var sectionsCompound = new NbtList("Sections", NbtTagType.Compound);
            foreach (var section in c.Sections)
            {
                if (section.YBase is null) { throw new InvalidOperationException("Section Ybase should not be null"); }//THIS should never happen

                var palatte = new NbtList("Palette", NbtTagType.Compound);

                if (section.Palette is LinearBlockStatePalette linear)
                {
                    foreach (var block in linear.BlockStateArray)
                    {
                        if (block is null)
                            continue;

                        palatte.Add(new NbtCompound//TODO redstone etc... has a lit metadata added when creating the palette
                            {
                                new NbtString("Name", block.UnlocalizedName)
                            });
                    }
                }

                var sec = new NbtCompound()
                    {
                        new NbtByte("Y", (byte)section.YBase),
                        palatte,
                        new NbtLongArray("BlockStates", section.BlockStorage.Storage)
                    };
                sectionsCompound.Add(sec);
            }

            var blocksCompound = new NbtList("Blocks", NbtTagType.Compound);
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
                    sectionsCompound, // Do we even use sections?
                    blocksCompound
                };

            chunkFile.RootTag = chunkCompound;
            chunkFile.SaveToFile(chunkPath, NbtCompression.GZip);
        }

        public void FlushChunks()
        {
            foreach (var c in LoadedChunks)
            {
                FlushChunk(c);
            }
        }
    }
}
