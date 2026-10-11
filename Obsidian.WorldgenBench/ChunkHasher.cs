using Obsidian.API;
using Obsidian.Nbt;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Runtime.InteropServices;

namespace WorldgenBench;

/// <summary>
/// Hashes generated chunks, so a change can show it leaves the output alone. Chunks must be added in the same order every
/// run.
/// </summary>
internal sealed class ChunkHasher
{
    private readonly XxHash64 values = new();
    private readonly XxHash64 saved = new();
    private readonly List<int> buffer = new(200_000);

    /// <summary>
    /// The hash of every chunk's blocks, light, biomes and heightmaps.
    /// </summary>
    public string Hash => this.values.GetCurrentHashAsUInt64().ToString("x16");

    /// <summary>
    /// The hash of every chunk as saved, which also covers block entities, scheduled ticks, entities and structure data.
    /// </summary>
    public string NbtHash => this.saved.GetCurrentHashAsUInt64().ToString("x16");

    public void Add(IChunk chunk)
    {
        this.buffer.Clear();
        for (var y = chunk.MinY; y < chunk.MinY + chunk.Height; y++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                {
                    this.buffer.Add(chunk.GetBlock(x, y, z).GetHashCode());
                    this.buffer.Add(chunk.GetLightLevel(x, y, z, LightType.Sky) | chunk.GetLightLevel(x, y, z, LightType.Block) << 4);
                }
            }
        }

        for (var y = chunk.MinY; y < chunk.MinY + chunk.Height; y += 4)
        {
            for (var z = 0; z < 16; z += 4)
            {
                for (var x = 0; x < 16; x += 4)
                    this.buffer.Add(chunk.GetBiome(x, y, z).Id);
            }
        }

        foreach (var (type, heightmap) in chunk.Heightmaps.OrderBy(entry => entry.Key))
        {
            this.buffer.Add((int)type);
            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                    this.buffer.Add(heightmap.GetHeight(x, z));
            }
        }

        var chunkHash = XxHash64.HashToUInt64(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(this.buffer)));
        this.values.Append(BitConverter.GetBytes(chunkHash));

        using var stream = new MemoryStream();
        using (var writer = new NbtWriterStream(stream, ""))
        {
            Region.SerializeChunk(writer, chunk, null);
            writer.EndCompound();
        }

        this.saved.Append(BitConverter.GetBytes(XxHash64.HashToUInt64(stream.ToArray())));
    }
}
