using Microsoft.Extensions.Logging;
using Obsidian.ChunkData;
using Obsidian.Nbt;
using Obsidian.Utilities.Collections;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Obsidian.WorldData;

public class Region : IRegion
{
    public const int CubicRegionSizeShift = 5;
    public const int CubicRegionSize = 1 << CubicRegionSizeShift;

    public int X { get; }
    public int Z { get; }

    public bool IsDirty { get; private set; } = true;

    public string RegionFolder { get; }

    public NbtCompression ChunkCompression { get; }

    public ConcurrentDictionary<int, IEntity> Entities { get; } = new();

    public int LoadedChunkCount => loadedChunks.Count(c => c.IsGenerated);

    private DenseCollection<IChunk> loadedChunks { get; } = new(CubicRegionSize, CubicRegionSize);

    private readonly RegionFile regionFile;

    private readonly ConcurrentDictionary<Vector, IBlockUpdate> blockUpdates = new();

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

    public void AddBlockUpdate(IBlockUpdate bu)
    {
        if (!blockUpdates.TryAdd(bu.Position, bu))
        {
            blockUpdates[bu.Position] = bu;
        }
    }

    public async Task<bool> InitAsync() => await regionFile.InitializeAsync();

    public async Task FlushAsync(CancellationToken cts = default)
    {
        foreach (Chunk c in loadedChunks.Cast<Chunk>())
            await SerializeChunkAsync(c);

        regionFile.Flush();
    }

    public async ValueTask<IChunk> GetChunkAsync(int x, int z)
    {
        var chunk = loadedChunks[x, z];
        if (chunk is null)
        {
            chunk = await GetChunkFromFileAsync(x, z); // Still might be null but that's okay.
            loadedChunks[x, z] = chunk!;
        }

        return chunk!;
    }

    public async Task UnloadChunk(int x, int z)
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

    public IEnumerable<IChunk> GeneratedChunks()
    {
        foreach (var c in loadedChunks)
        {
            if (c is not null && c.IsGenerated)
            {
                yield return c;
            }
        }
    }

    public void SetChunk(IChunk chunk)
    {
        if (chunk is null) { return; } // I dunno... maybe we'll need to null out a chunk someday?
        var (x, z) = (NumericsHelper.Modulo(chunk.X, CubicRegionSize), NumericsHelper.Modulo(chunk.Z, CubicRegionSize));
        loadedChunks[x, z] = chunk;
    }

    internal async Task SerializeChunkAsync(IChunk chunk)
    {
        var (x, z) = (NumericsHelper.Modulo(chunk.X, CubicRegionSize), NumericsHelper.Modulo(chunk.Z, CubicRegionSize));

        await using MemoryStream strm = new();
        await using NbtWriterStream writer = new(strm, ChunkCompression, "");

        SerializeChunk(writer, chunk);

        writer.EndCompound();

        await writer.TryFinishAsync();

        await regionFile.SetChunkAsync(x, z, strm.ToArray());
    }

    public async Task BeginTickAsync(CancellationToken cts = default)
    {
        await Parallel.ForEachAsync(Entities.Values, cts, async (entity, cts) => await entity.TickAsync());

        List<IBlockUpdate> neighborUpdates = [];
        List<IBlockUpdate> delayed = [];

        foreach (var pos in blockUpdates.Keys)
        {
            blockUpdates.Remove(pos, out var bu);
            if (bu.DelayCounter > 0)
            {
                bu.DelayCounter--;
                delayed.Add(bu);
            }
            else
            {
                bool updateNeighbor = await bu.World.HandleBlockUpdateAsync(bu);
                if (updateNeighbor) { neighborUpdates.Add(bu); }
            }
        }
        delayed.ForEach(AddBlockUpdate);
        neighborUpdates.ForEach(async u => await u.World.BlockUpdateNeighborsAsync(u));
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

            if (statesCompound.TryGetTag("data", out var dataArrayTag))
            {
                var data = dataArrayTag as NbtArray<long>;
                section.BlockStateContainer.DataArray.storage = data!.GetArray();
            }

            var biomesCompound = sectionCompound["biomes"] as NbtCompound;
            if (biomesCompound!.TryGetTag<NbtList>("palette", out var biomesPalette))
            {
                var biomePalette = section.BiomeContainer.Palette;
                foreach (NbtTag<string> biome in biomesPalette!)
                {
                    if (Enum.TryParse<Biome>(biome.Value.TrimResourceTag(), true, out var value))
                        biomePalette.GetOrAddId(value);
                }

                section.BiomeContainer.GrowDataArray();
            }

            if (biomesCompound.TryGetTag("data", out var biomeDataArrayTag))
            {
                var data = biomeDataArrayTag as NbtArray<long>;
                section.BiomeContainer.DataArray.storage = data!.GetArray();
            }


            if (sectionCompound.TryGetTag("SkyLight", out var skyLightTag))
            {
                var array = (NbtArray<byte>)skyLightTag;

                section.SetLight(array.GetArray(), LightType.Sky);
            }

            if (sectionCompound.TryGetTag("BlockLight", out var blockLightTag))
            {
                var array = (NbtArray<byte>)blockLightTag;

                section.SetLight(array.GetArray(), LightType.Block);
            }
        }

        foreach (var (name, heightmap) in (NbtCompound)chunkCompound["Heightmaps"])
        {
            var heightmapType = (HeightmapType)Enum.Parse(typeof(HeightmapType), name.Replace("_", ""), true);
            chunk.Heightmaps[heightmapType].data.storage = ((NbtArray<long>)heightmap).GetArray();
        }

        foreach (var tileEntityNbt in (NbtList)chunkCompound["block_entities"])
        {
            //TODO convert nbt tile entity to its respective type
            //var tileEntityCompound = tileEntityNbt as NbtCompound;

            //chunk.SetBlockEntity(tileEntityCompound.GetInt("x"), tileEntityCompound.GetInt("y"), tileEntityCompound.GetInt("z"), tileEntityCompound);
        }

        chunk.SetChunkStatus((ChunkGenStage)(Enum.TryParse(typeof(ChunkGenStage), chunkCompound.GetString("Status"), out var status) ? status : ChunkGenStage.empty));

        return chunk;
    }

    private static void SerializeChunk(NbtWriterStream writer, IChunk chunk)
    {
        writer.WriteListStart("sections", NbtTagType.Compound, chunk.Sections.Length);

        foreach (var section in chunk.Sections)
        {
            if (section.YBase is null)
                throw new UnreachableException("Section Ybase should not be null");//THIS should never happen

            writer.WriteCompoundStart();

            writer.WriteCompoundStart("block_states");

            if (section.BlockStateContainer.Palette is IndirectPalette indirect)
            {
                writer.WriteListStart("palette", NbtTagType.Compound, indirect.Count);

                Span<int> span = indirect.Values;
                for (int i = 0; i < indirect.Count; i++)
                {
                    var id = span[i];
                    var block = BlocksRegistry.Get(id);

                    writer.WriteCompoundStart();

                    writer.WriteString("Name", block.UnlocalizedName);
                    writer.WriteInt("Id", id);

                    writer.EndCompound();//TODO INCLUDE PROPERTIES
                }

                writer.EndList();

                writer.WriteArray("data", section.BlockStateContainer.DataArray.storage);
            }

            writer.EndCompound();

            writer.WriteCompoundStart("biomes");

            if (section.BiomeContainer.Palette is BaseIndirectPalette<Biome> indirectBiomePalette)
            {
                writer.WriteListStart("palette", NbtTagType.String, indirectBiomePalette.Count);

                Span<int> span = indirectBiomePalette.Values;
                for (int i = 0; i < indirectBiomePalette.Count; i++)
                {
                    var biome = (Biome)span[i];
                    writer.WriteString($"minecraft:{biome.ToString().ToLower()}");
                }

                writer.EndList();

                if (indirectBiomePalette.Values.Length > 1)
                    writer.WriteArray("data", section.BiomeContainer.DataArray.storage);
            }

            writer.EndCompound();

            writer.WriteByte("Y", (byte)section.YBase);
            writer.WriteArray("SkyLight", section.SkyLightArray.ToArray());
            writer.WriteArray("BlockLight", section.BlockLightArray.ToArray());

            writer.EndCompound();
        }
        writer.EndList();

        //TODO COME BACK TO THIS
        writer.WriteListStart("block_entities", NbtTagType.Compound, 0);
        //foreach (var (_, blockEntity) in chunk.BlockEntities)//
        //    writer.WriteTag(blockEntity);
        writer.EndList();

        writer.WriteInt("xPos", chunk.X);
        writer.WriteInt("zPos", chunk.Z);
        writer.WriteInt("yPos", -4);
        writer.WriteInt("DataVersion", 3337);
        writer.WriteString("Status", chunk.ChunkStatus.ToString());

        writer.WriteCompoundStart("Heightmaps");
        writer.WriteArray("MOTION_BLOCKING", chunk.Heightmaps[HeightmapType.MotionBlocking].data.storage);
        //new NbtArray<long>("OCEAN_FLOOR", chunk.Heightmaps[HeightmapType.OceanFloor].data.Storage),
        //new NbtArray<long>("WORLD_SURFACE", chunk.Heightmaps[HeightmapType.WorldSurface].data.Storage),
        writer.EndCompound();
    }
    #endregion NBT Ops

    public async ValueTask DisposeAsync() => await regionFile.DisposeAsync();
}
