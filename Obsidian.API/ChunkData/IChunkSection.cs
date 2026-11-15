using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.API.ChunkData;

/// <summary>
/// Exposes the blocks, quart biomes and packed light of a 16-by-16-by-16-block chunk section.
/// </summary>
public interface IChunkSection
{
    /// <summary>
    /// Gets the vertical section coordinate, multiplied by 16 for world block Y, or <c>null</c> when unspecified.
    /// </summary>
    public int? YBase { get; }

    /// <summary>
    /// Gets the palette-backed storage for the section's 4,096 block states.
    /// </summary>
    public DataContainer<IBlock> BlockStateContainer { get; }
    /// <summary>
    /// Gets the palette-backed storage for the section's 64 quart biomes.
    /// </summary>
    public DataContainer<BiomeCodec> BiomeContainer { get; }

    /// <summary>
    /// Gets whether sky light has been recorded for the section.
    /// </summary>
    public bool HasSkyLight { get; }
    /// <summary>
    /// Gets sky light packed as two four-bit levels per byte.
    /// </summary>
    public ReadOnlyMemory<byte> SkyLightArray { get; }

    /// <summary>
    /// Gets whether block light has been recorded for the section.
    /// </summary>
    public bool HasBlockLight { get; }
    /// <summary>
    /// Gets block light packed as two four-bit levels per byte.
    /// </summary>
    public ReadOnlyMemory<byte> BlockLightArray { get; }

    /// <summary>
    /// Gets whether the section is tracked as containing only air.
    /// </summary>
    public bool IsEmpty { get; }

    /// <summary>
    /// Gets a block using section-local block coordinates, from 0 through 15 on each axis.
    /// </summary>
    public IBlock GetBlock(Vector position);
    /// <summary>
    /// Gets a block using section-local block coordinates, from 0 through 15 on each axis.
    /// </summary>
    public IBlock GetBlock(int x, int y, int z);

    /// <summary>
    /// Gets a biome using section-local quart coordinates, from 0 through 3 on each axis.
    /// </summary>
    public BiomeCodec GetBiome(Vector position);
    /// <summary>
    /// Gets a biome using section-local quart coordinates, from 0 through 3 on each axis.
    /// </summary>
    public BiomeCodec GetBiome(int x, int y, int z);

    /// <summary>
    /// Sets a block using section-local block coordinates, from 0 through 15 on each axis.
    /// </summary>
    public void SetBlock(Vector position, IBlock block);
    /// <summary>
    /// Sets a block using section-local block coordinates, from 0 through 15 on each axis.
    /// </summary>
    public void SetBlock(int x, int y, int z, IBlock block);

    /// <summary>
    /// Sets a biome using section-local quart coordinates, from 0 through 3 on each axis.
    /// </summary>
    public void SetBiome(Vector position, BiomeCodec biome);
    /// <summary>
    /// Sets a biome using section-local quart coordinates, from 0 through 3 on each axis.
    /// </summary>
    public void SetBiome(int x, int y, int z, BiomeCodec biome);

    /// <summary>
    /// Sets a four-bit light level at section-local block coordinates for the selected light type.
    /// </summary>
    public void SetLightLevel(Vector position, LightType lt, int level);
    /// <summary>
    /// Sets a four-bit light level at section-local block coordinates for the selected light type.
    /// </summary>
    public void SetLightLevel(int x, int y, int z, LightType lt, int level);

    /// <summary>
    /// Gets the light level from 0 through 15 at section-local block coordinates.
    /// </summary>
    public int GetLightLevel(Vector position, LightType lt);
    /// <summary>
    /// Gets the light level from 0 through 15 at section-local block coordinates.
    /// </summary>
    public int GetLightLevel(int x, int y, int z, LightType lt);


    /// <summary>
    /// Replaces the selected light type's packed nibble array, retaining a copy of the supplied data.
    /// </summary>
    public void SetLight(byte[] data, LightType lt);
    /// <summary>
    /// Sets every sky light level in the section to 15.
    /// </summary>
    public void FillSkyLight();
    /// <summary>
    /// Creates a section copy with independent block, biome and light storage.
    /// </summary>
    public IChunkSection Clone();
}
