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
using System.Diagnostics;
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
            RegionFolder = Path.Join(worldRegionsPath, "regions");
            Directory.CreateDirectory(RegionFolder);
            var regionFile = Path.Join(RegionFolder, $"{X}.{Z}.rgn");
            if (!File.Exists(regionFile)) { return; }

            var regionNbt = new NbtFile();
            regionNbt.LoadFromFile(regionFile);
            Load(regionNbt.RootTag);
        }

        internal async Task BeginTickAsync(CancellationToken cts)
        {
            double flushTime = 0;
            while (!cts.IsCancellationRequested || cancel)
            {
                await Task.Delay(20);

                foreach (var (_, entity) in this.Entities)
                    await entity.TickAsync();
                flushTime++;

                if (flushTime > 50 * 10) // Save every 10 seconds
                {
                    Flush();
                    flushTime = 0;
                }
            }
            Flush();
        }

        internal void Cancel() => this.cancel = true;

        public void Flush()
        {
            return;
            var regionPath = Path.Join(RegionFolder, $"{X}.{Z}.rgn");
            var regionCompound = GetNbt();
            var regionFile = new NbtFile();
            regionFile.RootTag = regionCompound;
            regionFile.SaveToFile(regionPath, NbtCompression.GZip);
        }

        public void Load(NbtCompound regionCompound)
        {
            var chunksNbt = regionCompound["Chunks"] as NbtList;
            foreach (var chunkNbt in chunksNbt)
            {
                var chunk = GetChunkFromNbt((NbtCompound) chunkNbt);
                var index = (Helpers.Modulo(chunk.X, CUBIC_REGION_SIZE), Helpers.Modulo(chunk.Z, CUBIC_REGION_SIZE));
                LoadedChunks[index.Item1, index.Item2] = chunk;
            }
        }
        #region FileStuff
        public Chunk GetChunkFromNbt(NbtCompound chunkCompound)
        {
            int x = chunkCompound["xPos"].IntValue;
            int z = chunkCompound["zPos"].IntValue;

            var chunk = new Chunk(x, z);

            foreach (var bc in chunkCompound["Blocks"] as NbtList)
            {
                var index = bc["index"].ShortValue;
                var bx = bc["X"].DoubleValue;
                var by = bc["Y"].DoubleValue;
                var bz = bc["Z"].DoubleValue;
                var id = bc["id"].IntValue;
                var mat = bc["material"].StringValue;

                SebastiansBlock block = Registry.GetBlock((Materials)Enum.Parse(typeof(Materials), mat));
                chunk.SetBlock((int)bx, (int)by, (int)bz, block);
            }

            foreach (var secCompound in chunkCompound["Sections"] as NbtList)
            {
                var secY = (int)secCompound["Y"].ByteValue;
                var states = (secCompound["BlockStates"] as NbtLongArray).Value;
                var palettes = secCompound["Palette"] as NbtList;

                chunk.Sections[secY].BlockStorage.Storage = states;

                var chunkSecPalette = (LinearBlockStatePalette)chunk.Sections[secY].Palette;
                var index = 0;
                foreach (var palette in palettes)
                {
                    var block = new SebastiansBlock(Registry.NumericToBase[palette["Id"].IntValue]);
                    chunkSecPalette.BlockStateArray.SetValue(block, index);
                    index++;
                }

                chunkSecPalette.BlockStateCount = index;
            }

            chunk.BiomeContainer.Biomes = (chunkCompound["Biomes"] as NbtIntArray).Value.ToList();

            foreach (var heightmap in chunkCompound["Heightmaps"] as NbtCompound)
            {
                var heightmapType = (HeightmapType)Enum.Parse(typeof(HeightmapType), heightmap.Name.Replace("_", ""), true);
                var values = ((NbtLongArray)heightmap).Value;
                chunk.Heightmaps[heightmapType].data.Storage = values;
            }

            return chunk;
        }

        public NbtCompound GetNbt()
        {
            var entitiesCompound = new NbtList("Entities"); //TODO: this

            var chunksCompound = new NbtList("Chunks", NbtTagType.Compound);
            foreach (var chunk in LoadedChunks)
            {
                var chunkNbt = GetNbtFromChunk(chunk);
                chunksCompound.Add(chunkNbt);
            };

            var regionCompound = new NbtCompound("Data")
            {
                new NbtInt("xPos", this.X),
                new NbtInt("zPos", this.Z),
                chunksCompound
            };

            return regionCompound;
        }

        public NbtCompound GetNbtFromChunk(Chunk c)
        {
            var sectionsCompound = new NbtList("Sections", NbtTagType.Compound);
            foreach (var section in c.Sections)
            {
                if (section.YBase is null) { throw new InvalidOperationException("Section Ybase should not be null"); }//THIS should never happen

                var palatte = new NbtList("Palette", NbtTagType.Compound);

                if (section.Palette is LinearBlockStatePalette linear)
                {
                    foreach (var block in linear.BlockStateArray)
                    {
                        if (block.Id == 0)
                            continue;

                        palatte.Add(new NbtCompound//TODO redstone etc... has a lit metadata added when creating the palette
                            {
                                new NbtString("Name", block.UnlocalizedName),
                                new NbtInt("Id", block.Id)
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
            for (int x = c.X * 16; x < c.X * 16 + 16; x++)
            {
                for (int z = c.Z * 16; z < c.Z * 16 + 16; z++)
                {
                    for (int y = 0; y < 256; y++)
                    {
                        var block = c.GetBlock(x, y, z);
                        var b = new NbtCompound()
                        {
                            new NbtShort("index", 0),
                            new NbtDouble("X", x),
                            new NbtDouble("Y", y),
                            new NbtDouble("Z", z),
                            new NbtInt("id", block.Id),
                            new NbtString("material", block.Name),
                        };
                        blocksCompound.Add(b);
                    }
                }
            }

            var chunkCompound = new NbtCompound()
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
            return chunkCompound;
        }
        #endregion FileStuff
    }
}
