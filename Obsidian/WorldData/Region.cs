using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Nbt.Tags;
using Obsidian.Utilities;
using Obsidian.Utilities.Collection;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.WorldData
{
    public class Region
    {
        public const int cubicRegionSizeShift = 3;
        public const int cubicRegionSize = 1 << cubicRegionSizeShift;

        private bool cancel = false;
        public int X { get; }
        public int Z { get; }

        public bool IsDirty { get; set; } = true;

        public string RegionFolder { get; }

        public ConcurrentDictionary<int, Entity> Entities { get; private set; } = new ConcurrentDictionary<int, Entity>();

        public DenseCollection<Chunk> LoadedChunks { get; private set; } = new DenseCollection<Chunk>(cubicRegionSize, cubicRegionSize);

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
                await Task.Delay(20, cts);

                await Task.WhenAll(Entities.Select(entityEntry => entityEntry.Value.TickAsync()));

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
                var index = (NumericsHelper.Modulo(chunk.X, cubicRegionSize), NumericsHelper.Modulo(chunk.Z, cubicRegionSize));
                LoadedChunks[index.Item1, index.Item2] = chunk;
            }
            regionNbt = null;
            regionCompound = null;
            GC.Collect();
            IsDirty = false;
        }

        #region File saving/loading
        public static Chunk GetChunkFromNbt(NbtCompound chunkCompound)
        {
            int x = chunkCompound["xPos"].IntValue;
            int z = chunkCompound["zPos"].IntValue;

            var chunk = new Chunk(x, z);

            foreach (var secCompound in chunkCompound["Sections"] as NbtList)
            {
                var secY = (int)secCompound["Y"].ByteValue;
                var states = (secCompound["BlockStates"] as NbtLongArray).Value;
                var palettes = secCompound["Palette"] as NbtList;

                chunk.Sections[secY].BlockStorage.Storage = states;

                var chunkSecPalette = (LinearBlockStatePalette)chunk.Sections[secY].Palette;
                foreach (var palette in palettes)
                {
                    var block = new Block(palette["Id"].IntValue);
                    chunkSecPalette.GetIdFromState(block);
                }
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

        public static NbtCompound GetNbtFromChunk(Chunk chunk)
        {
            var sectionsCompound = new NbtList("Sections", NbtTagType.Compound);
            foreach (var section in chunk.Sections)
            {
                if (section.YBase is null) { throw new InvalidOperationException("Section Ybase should not be null"); }//THIS should never happen

                var palette = new NbtList("Palette", NbtTagType.Compound);

                if (section.Palette is LinearBlockStatePalette linear)
                {
                    foreach (var block in linear.BlockStateArray)
                    {
                        if (block.Id == 0)
                            continue;

                        palette.Add(new NbtCompound//TODO redstone etc... has a lit metadata added when creating the palette
                            {
                                new NbtString("Name", block.UnlocalizedName),
                                new NbtInt("Id", block.StateId)
                            });
                    }
                }

                var sec = new NbtCompound()
                    {
                        new NbtByte("Y", (byte)section.YBase),
                        palette,
                        new NbtLongArray("BlockStates", section.BlockStorage.Storage)
                    };
                sectionsCompound.Add(sec);
            }

            var chunkCompound = new NbtCompound()
                {
                    new NbtInt("xPos", chunk.X),
                    new NbtInt("zPos", chunk.Z),
                    new NbtIntArray("Biomes", chunk.BiomeContainer.Biomes.ToArray()),
                    new NbtCompound("Heightmaps")
                    {
                        new NbtLongArray("MOTION_BLOCKING", chunk.Heightmaps[HeightmapType.MotionBlocking].data.Storage),
                        new NbtLongArray("OCEAN_FLOOR", chunk.Heightmaps[HeightmapType.OceanFloor].data.Storage),
                        new NbtLongArray("WORLD_SURFACE", chunk.Heightmaps[HeightmapType.WorldSurface].data.Storage),
                    },
                    sectionsCompound
                };
            return chunkCompound;
        }
        #endregion File saving/loading
    }
}
