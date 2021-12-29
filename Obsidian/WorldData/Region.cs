using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Utilities.Collection;
using System.IO;
using System.Threading;

namespace Obsidian.WorldData;

public class Region
{
    public const int cubicRegionSizeShift = 5;
    public const int cubicRegionSize = 1 << cubicRegionSizeShift;

    public int X { get; }
    public int Z { get; }

    public bool IsDirty { get; private set; } = true;

    public string RegionFolder { get; }

    public ConcurrentDictionary<int, Entity> Entities { get; } = new();

    public int LoadedChunkCount => loadedChunks.Count;

    private DenseCollection<Chunk> loadedChunks { get; } = new(cubicRegionSize, cubicRegionSize);

    private readonly RegionFile regionFile;

    private readonly ConcurrentDictionary<Vector, BlockUpdate> blockUpdates = new();

    internal Region(int x, int z, string worldRegionsPath)
    {
        X = x;
        Z = z;
        RegionFolder = Path.Join(worldRegionsPath, "regions");
        Directory.CreateDirectory(RegionFolder);
        var filePath = Path.Join(RegionFolder, $"{X}.{Z}.mca");
        regionFile = new RegionFile(filePath, cubicRegionSize);

    }

    internal void AddBlockUpdate(BlockUpdate bu)
    {
        if (!blockUpdates.TryAdd(bu.position, bu))
        {
            blockUpdates[bu.position] = bu;
        }
    }

    internal Task<bool> InitAsync() => regionFile.InitializeAsync();

    internal async Task FlushAsync()
    {
        foreach (Chunk c in loadedChunks)
        { 
            try
            {
                SerializeChunk(c);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        await regionFile.FlushToDiskAsync();
    }

    internal Chunk GetChunk((int X, int Z) relativePos) => GetChunk(relativePos.X, relativePos.Z);

    internal Chunk GetChunk(int relativeX, int relativeZ) => GetChunk(new Vector(relativeX, 0, relativeZ));

    internal Chunk GetChunk(Vector relativePosition)
    {
        var chunk = loadedChunks[relativePosition.X, relativePosition.Z];
        if (chunk is null)
        {
            chunk = GetChunkFromFile(relativePosition); // Still might be null but that's okay.
            loadedChunks[relativePosition.X, relativePosition.Z] = chunk;
        }
        return chunk;
    }

    private Chunk GetChunkFromFile(Vector relativePosition)
    {
        var compressedBytes = regionFile.GetChunkCompressedBytes(relativePosition);
        if (compressedBytes is null) { return null; }
        using Stream strm = new MemoryStream(compressedBytes);
        NbtReader reader = new(strm, NbtCompression.GZip);
        NbtCompound chunkNbt = reader.ReadNextTag() as NbtCompound;
        return GetChunkFromNbt(chunkNbt);
    }

    internal IEnumerable<Chunk> GeneratedChunks()
    {
        foreach (var c in loadedChunks)
        {
            if (c is not null && c.isGenerated)
            {
                yield return c;
            }
        }
    }

    internal void SetChunk(Chunk chunk)
    {
        if (chunk is null) { return; } // I dunno... maybe we'll need to null out a chunk someday?
        var (x, z) = (NumericsHelper.Modulo(chunk.X, cubicRegionSize), NumericsHelper.Modulo(chunk.Z, cubicRegionSize));
        loadedChunks[x, z] = chunk;
    }

    internal void SerializeChunk(Chunk chunk)
    {
        var relativePosition = new Vector(NumericsHelper.Modulo(chunk.X, cubicRegionSize), 0, NumericsHelper.Modulo(chunk.Z, cubicRegionSize));
        NbtCompound chunkNbt = GetNbtFromChunk(chunk);

        using MemoryStream strm = new();
        using NbtWriter writer = new(strm, NbtCompression.GZip);

        writer.WriteTag(chunkNbt);

        writer.TryFinish();

        regionFile.SetChunkCompressedBytes(relativePosition, strm.ToArray());
    }

    internal async Task BeginTickAsync(CancellationToken cts)
    {
        var timer = new BalancingTimer(50, cts);
        while (await timer.WaitForNextTickAsync())
        {
            await Task.WhenAll(Entities.Select(entityEntry => entityEntry.Value.TickAsync()));

            List<BlockUpdate> neighborUpdates = new();
            List<BlockUpdate> delayed = new();

            foreach (var pos in blockUpdates.Keys)
            {
                blockUpdates.Remove(pos, out var bu);
                if (bu.delayCounter > 0)
                {
                    bu.delayCounter--;
                    delayed.Add(bu);
                }
                else
                {
                    bool updateNeighbor = await bu.world.HandleBlockUpdate(bu);
                    if (updateNeighbor) { neighborUpdates.Add(bu); }
                }
            }
            delayed.ForEach(i => AddBlockUpdate(i));
            neighborUpdates.ForEach(u => u.world.BlockUpdateNeighbors(u));
        }
    }

    #region NBT Ops
    public static Chunk GetChunkFromNbt(NbtCompound chunkCompound)
    {
        int x = chunkCompound.GetInt("xPos");
        int z = chunkCompound.GetInt("zPos");

        var chunk = new Chunk(x, z)
        {
            isGenerated = true
        };

        foreach (var child in chunkCompound["Sections"] as NbtList)
        {
            if (child is not NbtCompound sectionCompound)
                throw new InvalidOperationException("Nbt Tag is not a compound.");

            var secY = (int)sectionCompound.GetByte("Y");

            secY = secY > 20 ? secY - 256 : secY;

            var statesCompound = sectionCompound["block_states"] as NbtCompound;
            var blockStatePalette = statesCompound!["Palette"] as NbtList;
            var data = statesCompound["data"] as NbtArray<long>;

            var section = chunk.Sections[secY + 4];

            section.BlockStateContainer.DataArray.storage = data.GetArray();

            var chunkSecPalette = section.BlockStateContainer.Palette;
            foreach (NbtCompound palette in blockStatePalette!)
            {
                var block = new Block(palette.GetInt("Id"));
                chunkSecPalette.GetOrAddId(block);
            }

            var biomesCompound = sectionCompound["biomes"] as NbtCompound;
            var biomesPalette = biomesCompound!["Palette"] as NbtList;

            var biomePalette = section.BiomeContainer.Palette;
            foreach (NbtTag<string> biome in biomesPalette!)
            {
                if (Enum.TryParse<Biomes>(biome.Value.TrimMinecraftTag(), true, out var value))
                    biomePalette.GetOrAddId(value);
            }
        }

        foreach (var (name, heightmap) in chunkCompound["Heightmaps"] as NbtCompound)
        {
            var heightmapType = (HeightmapType)Enum.Parse(typeof(HeightmapType), name.Replace("_", ""), true);
            chunk.Heightmaps[heightmapType].data.storage = ((NbtArray<long>)heightmap).GetArray();
        }

        return chunk;
    }

    public static NbtCompound GetNbtFromChunk(Chunk chunk)
    {
        var sectionsCompound = new NbtList(NbtTagType.Compound, "Sections");

        foreach (var section in chunk.Sections)
        {
            if (section.YBase is null) { throw new InvalidOperationException("Section Ybase should not be null"); }//THIS should never happen

            var biomesCompound = new NbtCompound("biomes");
            var blockStatesCompound = new NbtCompound("block_states")
            {
                new NbtArray<long>("data", section.BlockStateContainer.DataArray.storage)
            };

            if (section.BlockStateContainer.Palette is IndirectPalette<Block> indirect)
            {
                var palette = new NbtList(NbtTagType.Compound, "Palette");

                foreach (var stateId in indirect.Values)
                {
                    if (stateId == 0)
                        continue;

                    var block = new Block(stateId);

                    palette.Add(new NbtCompound
                    {
                        new NbtTag<string>("Name", block.UnlocalizedName),
                        new NbtTag<int>("Id", block.StateId)
                    });//TODO redstone etc... has a lit metadata added when creating the palette
                }

                blockStatesCompound.Add(palette);
            }

            if (section.BiomeContainer.Palette is BaseIndirectPalette<Biomes> indirectBiomePalette)
            {
                var palette = new NbtList(NbtTagType.String, "Palette");

                foreach (var id in indirectBiomePalette.Values)
                {
                    var biome = (Biomes)id;

                    palette.Add(new NbtTag<string>(string.Empty, $"minecraft:{biome.ToString().ToLower()}"));
                }

                biomesCompound.Add(palette);
            }

            sectionsCompound.Add(new NbtCompound
            {
                new NbtTag<byte>("Y", (byte)section.YBase),
                biomesCompound,
                blockStatesCompound
            });
        }

        return new NbtCompound
        {
            new NbtTag<int>("xPos", chunk.X),
            new NbtTag<int>("zPos", chunk.Z),
            new NbtCompound("Heightmaps")
            {
                new NbtArray<long>("MOTION_BLOCKING", chunk.Heightmaps[HeightmapType.MotionBlocking].data.storage),
                //new NbtArray<long>("OCEAN_FLOOR", chunk.Heightmaps[HeightmapType.OceanFloor].data.Storage),
                //new NbtArray<long>("WORLD_SURFACE", chunk.Heightmaps[HeightmapType.WorldSurface].data.Storage),
            },
            sectionsCompound,
            new NbtTag<int>("DataVersion", 2860)// Hardcoded version try to get data version through minecraft data and use data correctly
        };
    }
    #endregion NBT Ops
}
