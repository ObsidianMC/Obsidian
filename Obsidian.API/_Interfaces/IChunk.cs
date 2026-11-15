using Obsidian.API.ChunkData;
using Obsidian.API.Registry.Codecs.Biomes;
using System.ComponentModel;

namespace Obsidian.API;
/// <summary>
/// A 16-block-wide column of a level, from its minimum build height up. Positions passed to it are block
/// coordinates: x and z may be world or chunk-local, as only their low 4 bits are used.
/// </summary>
/// <remarks>
/// Changing a chunk directly doesn't tell players or update neighbouring blocks; use the level's methods for that.
/// </remarks>
public interface IChunk : Data.IAttachmentHolder
{
    /// <summary>
    /// Gets the chunk's x coordinate, in chunks (block x divided by 16, rounded down).
    /// </summary>
    public int X { get; }

    /// <summary>
    /// Gets the chunk's z coordinate, in chunks (block z divided by 16, rounded down).
    /// </summary>
    public int Z { get; }

    /// <summary>
    /// Gets a value indicating whether the chunk has finished generating (<see cref="ChunkStatus"/> is
    /// <see cref="ChunkGenStage.full"/>).
    /// </summary>
    public bool IsGenerated { get; }

    /// <summary>
    /// Gets how far the chunk's generation has got.
    /// </summary>
    public ChunkGenStage ChunkStatus { get; }

    /// <summary>
    /// The lowest block Y of the chunk, i.e. its dimension's minimum build height.
    /// </summary>
    public int MinY { get; }

    /// <summary>
    /// The number of block layers in the chunk, i.e. its dimension's build height.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the chunk's heightmaps by type.
    /// </summary>
    public IDictionary<HeightmapType, Heightmap> Heightmaps { get; }

    /// <summary>
    /// The chunk's sections from the bottom up. This is a view of the chunk's own sections, not a copy.
    /// </summary>
    public ReadOnlySpan<IChunkSection> Sections { get; }

    /// <summary>
    /// Gets the block at <paramref name="position"/>.
    /// </summary>
    /// <param name="position">The block's position.</param>
    public IBlock GetBlock(Vector position) => this.GetBlock(position.X, position.Y, position.Z);

    /// <summary>
    /// Gets the block at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>).
    /// </summary>
    /// <param name="x">The block's x coordinate.</param>
    /// <param name="y">The block's y coordinate, within the chunk's height.</param>
    /// <param name="z">The block's z coordinate.</param>
    public IBlock GetBlock(int x, int y, int z);
    /// <summary>
    /// Gets the biome at <paramref name="position"/>.
    /// </summary>
    /// <param name="position">The block's position.</param>
    public BiomeCodec GetBiome(Vector position) => this.GetBiome(position.X, position.Y, position.Z);

    /// <summary>
    /// Gets the biome of the 4x4x4 cell the block at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>)
    /// is in.
    /// </summary>
    /// <param name="x">The block's x coordinate.</param>
    /// <param name="y">The block's y coordinate, within the chunk's height.</param>
    /// <param name="z">The block's z coordinate.</param>
    public BiomeCodec GetBiome(int x, int y, int z);

    /// <inheritdoc cref="SetBiome(int, int, int, BiomeCodec)"/>
    public void SetBiome(Vector position, BiomeCodec biome) => this.SetBiome(position.X, position.Y, position.Z, biome);

    /// <summary>
    /// Sets the biome of the quart (the 4x4x4 block cell biomes are stored in) the block at <paramref name="x"/>,
    /// <paramref name="y"/>, <paramref name="z"/> is in. The chunk saves it, but players who have the chunk don't see
    /// it until they're sent the chunk again: to change biomes players can see, use
    /// <see cref="World.ILevel.FillBiomes"/>, which sends them the change.
    /// </summary>
    public void SetBiome(int x, int y, int z, BiomeCodec biome);

    /// <summary>
    /// Sets the block at <paramref name="position"/>. A position outside the chunk's height is ignored.
    /// </summary>
    /// <param name="position">The block's position.</param>
    /// <param name="block">The new block.</param>
    public void SetBlock(Vector position, IBlock block) => this.SetBlock(position.X, position.Y, position.Z, block);

    /// <summary>
    /// Sets the block at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>), updating the heightmaps.
    /// A <paramref name="y"/> outside the chunk's height is ignored.
    /// </summary>
    /// <param name="x">The block's x coordinate.</param>
    /// <param name="y">The block's y coordinate.</param>
    /// <param name="z">The block's z coordinate.</param>
    /// <param name="block">The new block.</param>
    public void SetBlock(int x, int y, int z, IBlock block);
    /// <summary>
    /// Sets the light level of one type at <paramref name="position"/>.
    /// </summary>
    /// <param name="position">The block's position.</param>
    /// <param name="lt">Whether to set sky or block light.</param>
    /// <param name="light">The light level, from 0 to 15.</param>
    public void SetLightLevel(Vector position, LightType lt, int light) => this.SetLightLevel(position.X, position.Y, position.Z, lt, light);
    /// <summary>
    /// Sets the light level of one type at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>).
    /// </summary>
    /// <param name="x">The block's x coordinate.</param>
    /// <param name="y">The block's y coordinate.</param>
    /// <param name="z">The block's z coordinate.</param>
    /// <param name="lt">Whether to set sky or block light.</param>
    /// <param name="level">The light level, from 0 to 15.</param>
    public void SetLightLevel(int x, int y, int z, LightType lt, int level);

    /// <summary>
    /// Gets the light level of one type at <paramref name="position"/>.
    /// </summary>
    /// <param name="position">The block's position.</param>
    /// <param name="lt">Whether to get sky or block light.</param>
    /// <returns>The light level, from 0 to 15.</returns>
    public int GetLightLevel(Vector position, LightType lt) => this.GetLightLevel(position.X, position.Y, position.Z, lt);
    /// <summary>
    /// Gets the light level of one type at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>).
    /// </summary>
    /// <param name="x">The block's x coordinate.</param>
    /// <param name="y">The block's y coordinate.</param>
    /// <param name="z">The block's z coordinate.</param>
    /// <param name="lt">Whether to get sky or block light.</param>
    /// <returns>The light level, from 0 to 15.</returns>
    public int GetLightLevel(int x, int y, int z, LightType lt);

    /// <summary>
    /// Gets the block entity at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>).
    /// </summary>
    /// <param name="x">The block's x coordinate.</param>
    /// <param name="y">The block's y coordinate.</param>
    /// <param name="z">The block's z coordinate.</param>
    /// <returns>The block entity, or <see langword="null"/> if there is none.</returns>
    public IBlockEntity GetBlockEntity(int x, int y, int z);
    /// <summary>
    /// Stores a block entity at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>), replacing any there.
    /// </summary>
    /// <param name="x">The block's x coordinate.</param>
    /// <param name="y">The block's y coordinate.</param>
    /// <param name="z">The block's z coordinate.</param>
    /// <param name="tileEntityData">The block entity.</param>
    public void SetBlockEntity(int x, int y, int z, IBlockEntity tileEntityData);
    /// <summary>
    /// Removes the block entity at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>), if there is one.
    /// </summary>
    /// <param name="x">The block's x coordinate.</param>
    /// <param name="y">The block's y coordinate.</param>
    /// <param name="z">The block's z coordinate.</param>
    public void RemoveBlockEntity(int x, int y, int z);

    /// <summary>
    /// Every block entity in the chunk.
    /// </summary>
    public IReadOnlyCollection<IBlockEntity> GetBlockEntities();

    /// <summary>
    /// Sets how far the chunk's generation has got. Used by world generation.
    /// </summary>
    /// <param name="status">The new stage.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void SetChunkStatus(ChunkGenStage status);

    /// <summary>
    /// Writes the bit set of sections with light of type <paramref name="lt"/>, for a light update packet.
    /// </summary>
    /// <param name="writer">The stream to write to.</param>
    /// <param name="lt">The light type.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void WriteLightMaskTo(INetStreamWriter writer, LightType lt);

    /// <summary>
    /// Writes the bit set of sections without light of type <paramref name="lt"/>, for a light update packet.
    /// </summary>
    /// <param name="writer">The stream to write to.</param>
    /// <param name="lt">The light type.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void WriteEmptyLightMaskTo(INetStreamWriter writer, LightType lt);

    /// <summary>
    /// Writes the light arrays of the sections with light of type <paramref name="lt"/>, prefixed with their count.
    /// </summary>
    /// <param name="writer">The stream to write to.</param>
    /// <param name="lt">The light type.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void WriteLightTo(INetStreamWriter writer, LightType lt);

    /// <summary>
    /// Copies the chunk's sections and heightmaps into a new chunk at another position.
    /// </summary>
    /// <param name="x">The new chunk's x coordinate, in chunks.</param>
    /// <param name="z">The new chunk's z coordinate, in chunks.</param>
    /// <returns>The copy.</returns>
    public IChunk Clone(int x, int z);
}
