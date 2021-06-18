using Obsidian.API;
using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.Nbt;
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

        public ConcurrentDictionary<int, Entity> Entities { get; private set; } = new();

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

        //TODO: IO operations should be async :teyes:
        public void Flush()
        {
            if (!IsDirty) { return; }

            var path = Path.Join(RegionFolder, $"{X}.{Z}.rgn");

            var regionFile = new FileInfo(path);
            FileInfo backupRegionFile = null;

            if (regionFile.Exists)
                backupRegionFile = regionFile.CopyTo($"{path}.bak", true);

            using var fileStream = regionFile.OpenWrite();

            var writer = new NbtWriter(fileStream, NbtCompression.GZip);

            writer.WriteTag(this.GetNbt());

            writer.BaseStream.Flush();

            writer.TryFinish();

            backupRegionFile?.Delete();

            regionFile = null;

            GC.Collect();

            IsDirty = false;
        }

        public void Load(string regionPath)
        {
            var regionFile = new FileInfo(regionPath);

            if (!regionFile.Exists)
            {
                File.Move(regionPath + ".bak", regionPath);

                regionFile = new FileInfo(regionPath + ".bak");
            }

            using var readStream = regionFile.OpenRead();

            var reader = new NbtReader(readStream, NbtCompression.GZip);

            var regionCompound = (NbtCompound)reader.ReadNextTag();

            var chunksNbt = regionCompound["Chunks"] as NbtList;

            foreach (var chunkNbt in chunksNbt)
            {
                var chunk = GetChunkFromNbt((NbtCompound)chunkNbt);
                var index = (NumericsHelper.Modulo(chunk.X, cubicRegionSize), NumericsHelper.Modulo(chunk.Z, cubicRegionSize));
                LoadedChunks[index.Item1, index.Item2] = chunk;
            }

            regionFile = null;
            regionCompound = null;

            GC.Collect();
            IsDirty = false;
        }

        #region File saving/loading
        public static Chunk GetChunkFromNbt(NbtCompound chunkCompound)
        {
            int x = chunkCompound.GetInt("xPos");
            int z = chunkCompound.GetInt("zPos");

            var chunk = new Chunk(x, z)
            {
                isGenerated = true
            };

            foreach (var secCompound in chunkCompound["Sections"] as NbtList)
            {
                var compound = secCompound as NbtCompound;
                var secY = (int)compound.GetByte("Y");
                var states = compound["BlockStates"] as NbtArray<long>;//TODO
                var palettes = compound["Palette"] as NbtList;

                chunk.Sections[secY].BlockStorage.Storage = states.GetArray();

                var chunkSecPalette = (LinearBlockStatePalette)chunk.Sections[secY].Palette;
                foreach (NbtCompound palette in palettes)
                {

                    var block = new Block(palette.GetInt("Id"));
                    chunkSecPalette.GetIdFromState(block);
                }
            }

            chunk.BiomeContainer.Biomes = (chunkCompound["Biomes"] as NbtArray<int>).GetArray().ToList();

            foreach (var (name, heightmap) in chunkCompound["Heightmaps"] as NbtCompound)
            {
                var heightmapType = (HeightmapType)Enum.Parse(typeof(HeightmapType), name.Replace("_", ""), true);
                chunk.Heightmaps[heightmapType].data.Storage = ((NbtArray<long>)heightmap).GetArray();
            }

            return chunk;
        }

        public NbtCompound GetNbt()
        {
            // var entitiesCompound = new NbtList("Entities"); //TODO: this

            var chunksCompound = new NbtList(NbtTagType.Compound, "Chunks");
            foreach (var chunk in LoadedChunks)
            {
                var chunkNbt = GetNbtFromChunk(chunk);
                chunksCompound.Add(chunkNbt);
            };

            var regionCompound = new NbtCompound("Data")
            {
                new NbtTag<int>("xPos", this.X),
                new NbtTag<int>("zPos", this.Z),
                chunksCompound
            };

            return regionCompound;
        }

        public static NbtCompound GetNbtFromChunk(Chunk chunk)
        {
            var sectionsCompound = new NbtList(NbtTagType.Compound, "Sections");
            foreach (var section in chunk.Sections)
            {
                if (section.YBase is null) { throw new InvalidOperationException("Section Ybase should not be null"); }//THIS should never happen

                var palette = new NbtList(NbtTagType.Compound, "Palette");

                if (section.Palette is LinearBlockStatePalette linear)
                {
                    foreach (var stateId in linear.BlockStateArray)
                    {
                        if (stateId == 0)
                            continue;

                        var block = new Block(stateId);

                        palette.Add(new NbtCompound//TODO redstone etc... has a lit metadata added when creating the palette
                            {
                                new NbtTag<string>("Name", block.UnlocalizedName),
                                new NbtTag<int>("Id", block.StateId)
                            });
                    }
                }

                var sec = new NbtCompound()
                    {
                        new NbtTag<byte>("Y", (byte)section.YBase),
                        palette,
                        new NbtArray<long>("BlockStates", section.BlockStorage.Storage)
                    };
                sectionsCompound.Add(sec);
            }

            var chunkCompound = new NbtCompound()
                {
                    new NbtTag<int>("xPos", chunk.X),
                    new NbtTag<int>("zPos", chunk.Z),
                    new NbtArray<int>("Biomes", chunk.BiomeContainer.Biomes),
                    new NbtCompound("Heightmaps")
                    {
                        new NbtArray<long>("MOTION_BLOCKING", chunk.Heightmaps[HeightmapType.MotionBlocking].data.Storage),
                        new NbtArray<long>("OCEAN_FLOOR", chunk.Heightmaps[HeightmapType.OceanFloor].data.Storage),
                        new NbtArray<long>("WORLD_SURFACE", chunk.Heightmaps[HeightmapType.WorldSurface].data.Storage),
                    },
                    sectionsCompound
                };
            return chunkCompound;
        }
        #endregion File saving/loading
    }
}
