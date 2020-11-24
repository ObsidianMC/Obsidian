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

        public bool IsDirty { get; set; } = true;

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
            if (File.Exists(regionFile))
            {
                Load(regionFile);
                IsDirty = false;
            }
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

                if (flushTime > 50 * 30) // Save every 30 seconds
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
            if (!IsDirty) { return; }
            var regionPath = Path.Join(RegionFolder, $"{X}.{Z}.rgn");
            if (File.Exists(regionPath))
                File.Copy(regionPath, regionPath + ".bak");
            
            var regionCompound = GetNbt();
            var regionFile = new NbtFile();
            regionFile.RootTag = regionCompound;
            regionFile.SaveToFile(regionPath, NbtCompression.GZip);
            
            File.Delete(regionPath + ".bak");
            regionCompound = null;
            regionFile = null;
            GC.Collect();
            IsDirty = false;
        }

        public void Load(string regionFile)
        {
            var regionNbt = new NbtFile();
            try
            {
                regionNbt.LoadFromFile(regionFile);
            }
            catch (Exception)
            {
                File.Delete(regionFile);
                File.Move(regionFile + ".bak", regionFile);
                regionNbt.LoadFromFile(regionFile);
            }
            finally
            {
                File.Delete(regionFile + ".bak");
            }

            NbtCompound regionCompound = regionNbt.RootTag;
            var chunksNbt = regionCompound["Chunks"] as NbtList;
            foreach (var chunkNbt in chunksNbt)
            {
                var chunk = GetChunkFromNbt((NbtCompound) chunkNbt);
                var index = (Helpers.Modulo(chunk.X, CUBIC_REGION_SIZE), Helpers.Modulo(chunk.Z, CUBIC_REGION_SIZE));
                LoadedChunks[index.Item1, index.Item2] = chunk;
            }
            regionNbt = null;
            regionCompound = null;
            GC.Collect();
            IsDirty = false;
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

                Block block = Registry.GetBlock((Materials)Enum.Parse(typeof(Materials), mat));
                block.Location = new Position(bx, by, bz);
                chunk.Blocks.Add(index, block);
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
                    var block = Registry.GetBlock(palette["Name"].StringValue);
                    if (block is null) { continue; }
                    block.Id = palette["Id"].IntValue;
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
                        if (block is null)
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
