using Obsidian.API;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Mojang;
using System.Collections.Generic;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Shares chunk builders and generated chunks between the vanilla parity tests, since building the noise and generating
/// full chunks dominate their cost.
/// </summary>
/// <remarks>
/// Tests using it are in <see cref="WorldgenCollection"/>, so they run one at a time and can share chunks.
/// </remarks>
public sealed class WorldgenFixture
{
    private readonly Dictionary<(MojangDimension Dimension, long Seed, bool Structures), ChunkBuilder> builders = [];
    private readonly Dictionary<ChunkBuilder, FullChunks> fullChunks = [];

    /// <summary>
    /// The builder for a world, created on first use.
    /// </summary>
    /// <param name="structures">Whether structures are generated; golden chunks from vanilla's chunk steps have none.</param>
    internal ChunkBuilder Builder(MojangDimension dimension, long seed, bool structures = true)
    {
        if (!this.builders.TryGetValue((dimension, seed, structures), out var builder))
            this.builders[(dimension, seed, structures)] = builder = new ChunkBuilder(dimension, seed, structures);

        return builder;
    }

    /// <summary>
    /// The full chunks of a world, generated on demand and kept for later tests.
    /// </summary>
    internal FullChunks Chunks(MojangDimension dimension, long seed, bool structures = true)
    {
        var builder = this.Builder(dimension, seed, structures);
        if (!this.fullChunks.TryGetValue(builder, out var chunks))
            this.fullChunks[builder] = chunks = new FullChunks(builder, dimension);

        return chunks;
    }
}

[CollectionDefinition(Name)]
public sealed class WorldgenCollection : ICollectionFixture<WorldgenFixture>
{
    public const string Name = "Worldgen";
}

/// <summary>
/// Generates full chunks on demand like a server: chunks are carved, decorated once all their neighbors are carved, then
/// post-processed once all their neighbors are decorated.
/// </summary>
internal sealed class FullChunks(ChunkBuilder builder, MojangDimension dimension)
{
    private readonly Dictionary<(int X, int Z), IChunk> carved = [];
    private readonly HashSet<(int X, int Z)> decorated = [];
    private readonly HashSet<(int X, int Z)> finished = [];

    /// <summary>
    /// The chunk once complete: its neighbors decorated, then its post-processing (shape updates) and final heightmaps.
    /// </summary>
    public IChunk Get(int chunkX, int chunkZ)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
                this.Decorate(chunkX + dx, chunkZ + dz);
        }

        var chunk = this.Carve(chunkX, chunkZ);
        if (this.finished.Add((chunkX, chunkZ)))
        {
            builder.PostProcess(this.Area(chunkX, chunkZ), chunkX, chunkZ);
            builder.UpdateFinalHeightmaps(chunk);
        }

        return chunk;
    }

    private Dictionary<(int X, int Z), IChunk> Area(int chunkX, int chunkZ)
    {
        var area = new Dictionary<(int X, int Z), IChunk>();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
                area[(chunkX + dx, chunkZ + dz)] = this.Carve(chunkX + dx, chunkZ + dz);
        }

        return area;
    }

    private void Decorate(int chunkX, int chunkZ)
    {
        if (this.decorated.Add((chunkX, chunkZ)))
            builder.Decorate(this.Area(chunkX, chunkZ), chunkX, chunkZ);
    }

    private IChunk Carve(int chunkX, int chunkZ)
    {
        if (this.carved.TryGetValue((chunkX, chunkZ), out var chunk))
            return chunk;

        chunk = new Chunk(chunkX, chunkZ, dimension.MinY, dimension.Height);
        builder.PopulateBiomes(chunk);
        builder.Generate3DTerrain(chunk);
        builder.ApplySurfaceRules(chunk);
        builder.ApplyCarvers(chunk);
        this.carved[(chunkX, chunkZ)] = chunk;
        return chunk;
    }
}
