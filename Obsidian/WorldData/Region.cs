using Microsoft.Extensions.Logging;
using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Utilities.Collection;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Obsidian.WorldData;

public class Region : IAsyncDisposable
{
    public const int CubicRegionSizeShift = 5;
    public const int CubicRegionSize = 1 << CubicRegionSizeShift;

    public int X { get; }
    public int Z { get; }

    public bool IsDirty { get; private set; } = true;

    public string RegionFolder { get; }

    public NbtCompression ChunkCompression { get; }

    public ConcurrentDictionary<int, Entity> Entities { get; } = new();

    public int LoadedChunkCount => loadedChunks.Count(c => c.IsGenerated);

    private DenseCollection<Chunk> loadedChunks { get; } = new(CubicRegionSize, CubicRegionSize);

    private readonly RegionFile regionFile;

    private readonly ConcurrentDictionary<Vector, BlockUpdate> blockUpdates = new();

    internal Region(int x, int z, string worldFolderPath, NbtCompression chunkCompression = NbtCompression.ZLib,
        ILogger? logger = null)
    {
        X = x;
        Z = z;
        RegionFolder = Path.Join(worldFolderPath, "regions");
        Directory.CreateDirectory(RegionFolder);
        var filePath = Path.Join(RegionFolder, $"r.{X}.{Z}.mca");
        regionFile = new RegionFile(filePath, chunkCompression, CubicRegionSize, logger);
        ChunkCompression = chunkCompression;
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
            await SerializeChunkAsync(c);

        await regionFile.FlushAsync();
    }

    internal async ValueTask<Chunk> GetChunkAsync(int x, int z)
    {
        var chunk = loadedChunks[x, z];
        if (chunk is null)
        {
            chunk = await GetChunkFromFileAsync(x, z); // Still might be null but that's okay.
            loadedChunks[x, z] = chunk!;
        }

        return chunk!;
    }

    internal async Task UnloadChunk(int x, int z)
    {
        var chunk = loadedChunks[x, z];
        if (chunk is null) { return; }
        await SerializeChunkAsync(chunk);
        loadedChunks[x, z] = null;
    }

    private async Task<Chunk?> GetChunkFromFileAsync(int x, int z)
    {
        var chunkBuffer = await regionFile.GetChunkBytesAsync(x, z);

        if (chunkBuffer is not Memory<byte> chunkData)
            return null;

        await using var bytesStream = new ReadOnlyStream(chunkData);
        var nbtReader = new NbtReader(bytesStream);

        return DeserializeChunk(nbtReader.ReadNextTag() as NbtCompound);
    }

    internal IEnumerable<Chunk> GeneratedChunks()
    {
        foreach (var c in loadedChunks)
        {
            if (c is not null && c.IsGenerated)
            {
                yield return c;
            }
        }
    }

    internal void SetChunk(Chunk chunk)
    {
        if (chunk is null) { return; } // I dunno... maybe we'll need to null out a chunk someday?
        var (x, z) = (NumericsHelper.Modulo(chunk.X, CubicRegionSize), NumericsHelper.Modulo(chunk.Z, CubicRegionSize));
        loadedChunks[x, z] = chunk;
    }

    internal async Task SerializeChunkAsync(Chunk chunk)
    {
        NbtCompound chunkNbt = SerializeChunk(chunk);

        var (x, z) = (NumericsHelper.Modulo(chunk.X, CubicRegionSize), NumericsHelper.Modulo(chunk.Z, CubicRegionSize));

        await using MemoryStream strm = new();
        await using NbtWriter writer = new(strm, ChunkCompression);

        writer.WriteTag(chunkNbt);

        await writer.TryFinishAsync();

        await regionFile.SetChunkAsync(x, z, strm.ToArray());
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
                    bool updateNeighbor = await bu.world.HandleBlockUpdateAsync(bu);
                    if (updateNeighbor) { neighborUpdates.Add(bu); }
                }
            }
            delayed.ForEach(i => AddBlockUpdate(i));
            neighborUpdates.ForEach(async u => await u.world.BlockUpdateNeighborsAsync(u));
        }
    }

    #region NBT Ops
    private static Chunk DeserializeChunk(NbtCompound chunkCompound)
    {
        int x = chunkCompound.GetInt("xPos");
        int z = chunkCompound.GetInt("zPos");

        var chunk = new Chunk(x, z);

        foreach (var child in (NbtList)chunkCompound["sections"])
        {
            if (child is not NbtCompound sectionCompound)
                throw new InvalidOperationException("Nbt Tag is not a compound.");

            var secY = (int)sectionCompound.GetByte("Y");

            secY = secY > 20 ? secY - 256 : secY;

            if (!sectionCompound.TryGetTag("block_states", out var statesTag))
                throw new UnreachableException("Unable to find block states from NBT.");

            var statesCompound = statesTag as NbtCompound;

            var section = chunk.Sections[secY + 4];

            var chunkSecPalette = section.BlockStateContainer.Palette;

            if (statesCompound!.TryGetTag("palette", out var palleteArrayTag))
            {
                var blockStatesPalette = palleteArrayTag as NbtList;
                foreach (NbtCompound entry in blockStatesPalette!)
                {
                    var id = entry.GetInt("Id");

                    chunkSecPalette.GetOrAddId(BlocksRegistry.Get(id));//TODO PROCESS ADDED PROPERTIES TO GET CORRECT BLOCK STATE
                }

                section.BlockStateContainer.GrowDataArray();
            }

            //TODO find a way around this (We shouldn't be storing the states data array in nbt anyway.)
            if (statesCompound.TryGetTag("data", out var dataArrayTag))
            {
                var data = dataArrayTag as NbtArray<long>;

                section.BlockStateContainer.DataArray.storage = data!.GetArray();
            }

            var biomesCompound = sectionCompound["biomes"] as NbtCompound;
            var biomesPalette = biomesCompound!["palette"] as NbtList;

            var biomePalette = section.BiomeContainer.Palette;
            foreach (NbtTag<string> biome in biomesPalette!)
            {
                if (Enum.TryParse<Biome>(biome.Value.TrimResourceTag(), true, out var value))
                    biomePalette.GetOrAddId(value);
            }

            if (sectionCompound.TryGetTag("SkyLight", out var skyLightTag))
            {
                var array = (NbtArray<byte>)skyLightTag;

                section.SetLight(array.GetArray(), LightType.Sky);
            }

            if (sectionCompound.TryGetTag("BlockLight", out var blockLightTag))
            {
                var array = (NbtArray<byte>)blockLightTag;

                section.SetLight(array.GetArray(), LightType.Sky);
            }
        }

        foreach (var (name, heightmap) in (NbtCompound)chunkCompound["Heightmaps"])
        {
            var heightmapType = (HeightmapType)Enum.Parse(typeof(HeightmapType), name.Replace("_", ""), true);
            chunk.Heightmaps[heightmapType].data.storage = ((NbtArray<long>)heightmap).GetArray();
        }

        foreach (var tileEntityNbt in (NbtList)chunkCompound["block_entities"])
        {
            var tileEntityCompound = tileEntityNbt as NbtCompound;

            chunk.SetBlockEntity(tileEntityCompound.GetInt("x"), tileEntityCompound.GetInt("y"), tileEntityCompound.GetInt("z"), tileEntityCompound);
        }

        chunk.chunkStatus = (ChunkStatus)(Enum.TryParse(typeof(ChunkStatus), chunkCompound.GetString("Status"), out var status) ? status : ChunkStatus.empty);

        return chunk;
    }

    private static NbtCompound SerializeChunk(Chunk chunk)
    {
        var sectionsCompound = new NbtList(NbtTagType.Compound, "sections");

        foreach (var section in chunk.Sections)
        {
            if (section.YBase is null)
                throw new UnreachableException("Section Ybase should not be null");//THIS should never happen

            var biomesCompound = new NbtCompound("biomes");
            var blockStatesCompound = new NbtCompound("block_states");

            if (section.BlockStateContainer.DataArray.storage.Any(x => x > 0))
                blockStatesCompound.Add(new NbtArray<long>("data", section.BlockStateContainer.DataArray.storage));

            if (section.BlockStateContainer.Palette is IndirectPalette indirect)
            {
                var palette = new NbtList(NbtTagType.Compound, "palette");

                Span<int> span = indirect.Values;
                for (int i = 0; i < indirect.Count; i++)
                {
                    var id = span[i];
                    var block = BlocksRegistry.Get(id);

                    palette.Add(new NbtCompound
                    {
                        new NbtTag<string>("Name", block.UnlocalizedName),
                        new NbtTag<int>("Id", id)
                    });//TODO INCLUDE PROPERTIES
                }

                blockStatesCompound.Add(palette);
            }

            if (section.BiomeContainer.Palette is BaseIndirectPalette<Biome> indirectBiomePalette)
            {
                var palette = new NbtList(NbtTagType.String, "palette");

                foreach (var id in indirectBiomePalette.Values)
                {
                    var biome = (Biome)id;

                    palette.Add(new NbtTag<string>(string.Empty, $"minecraft:{biome.ToString().ToLower()}"));
                }

                biomesCompound.Add(palette);
            }

            sectionsCompound.Add(new NbtCompound
            {
                new NbtTag<byte>("Y", (byte)section.YBase),
                blockStatesCompound,
                biomesCompound,
                new NbtArray<byte>("SkyLight", section.SkyLightArray.ToArray()),
                new NbtArray<byte>("BlockLight", section.BlockLightArray.ToArray())
            });
        }

        var blockEntities = new NbtList(NbtTagType.Compound, "block_entities");
        foreach (var (_, blockEntity) in chunk.BlockEntities)
            blockEntities.Add(blockEntity);

        return new NbtCompound
        {
            new NbtTag<int>("xPos", chunk.X),
            new NbtTag<int>("zPos", chunk.Z),
            new NbtTag<int>("yPos", -4),
            new NbtTag<string>("Status", chunk.chunkStatus.ToString()),
            new NbtCompound("Heightmaps")
            {
                new NbtArray<long>("MOTION_BLOCKING", chunk.Heightmaps[HeightmapType.MotionBlocking].data.storage),
                //new NbtArray<long>("OCEAN_FLOOR", chunk.Heightmaps[HeightmapType.OceanFloor].data.Storage),
                //new NbtArray<long>("WORLD_SURFACE", chunk.Heightmaps[HeightmapType.WorldSurface].data.Storage),
            },
            blockEntities,
            sectionsCompound,
            new NbtTag<int>("DataVersion", 3337)// Hardcoded version try to get data version through minecraft data and use data correctly
        };
    }
    #endregion NBT Ops

    public async ValueTask DisposeAsync() => await regionFile.DisposeAsync();
}
