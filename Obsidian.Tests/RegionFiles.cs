using Obsidian.API;
using Obsidian.ChunkData;
using Obsidian.Registries;
using Obsidian.WorldData;
using Obsidian.Nbt;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Region files stay in vanilla's layout however Obsidian rewrites them, keep a chunk's previous version when a write
/// doesn't finish, reuse the sectors chunks leave, and write each chunk's versions in order under concurrent saves.
/// </summary>
public sealed class RegionFiles : IDisposable
{
    private const int SectorSize = 4096;

    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-region-files-{Guid.NewGuid():N}");

    public RegionFiles() => Directory.CreateDirectory(this.folder);

    public void Dispose() => Directory.Delete(this.folder, recursive: true);

    [Fact]
    public async Task RewrittenVanillaRegionKeepsVanillaLayout()
    {
        var path = Path.Join(this.folder, "r.0.0.mca");
        File.Copy(FixtureRegion(), path);

        // The vanilla chunk, then rewritten grown, shrunk and oversized (in its external file) at other positions,
        // each round flushed like an autosave.
        var expected = new Dictionary<(int X, int Z), byte[]>();
        await using (var file = await OpenAsync(path))
        {
            var vanilla = (await file.GetChunkBytesAsync(0, 0))!.Value.ToArray();
            int[] fillers = [0, 40_000, 2_000, 1_500_000, 10];
            for (var round = 0; round < fillers.Length; round++)
            {
                foreach (var position in new[] { (0, 0), (1, 0), (31, 31) })
                {
                    var filler = position == (31, 31) ? fillers[round] : fillers[^(round + 1)];
                    expected[position] = WithFiller(vanilla, filler, seed: round);
                    await WriteAsync(file, position.Item1, position.Item2, expected[position]);
                }

                await file.FlushAsync();
            }

            // Still external at the end, once.
            expected[(5, 7)] = WithFiller(vanilla, 1_200_000, seed: 99);
            await WriteAsync(file, 5, 7, expected[(5, 7)]);
        }

        Assert.Equal(expected.Keys.Order(), ReadVanillaLayout(path).Keys.Order());
        foreach (var (position, chunk) in ReadVanillaLayout(path))
            Assert.Equal(expected[position], chunk);

        Assert.Equal(["c.5.7.mcc"], Directory.GetFiles(this.folder, "*.mcc").Select(Path.GetFileName));

        await using var reopened = await OpenAsync(path);
        foreach (var (position, chunk) in expected)
            Assert.Equal(chunk, (await reopened.GetChunkBytesAsync(position.X, position.Z))!.Value.ToArray());
    }

    [Fact]
    public async Task InterruptedWriteLeavesThePreviousVersion()
    {
        var path = Path.Join(this.folder, "r.0.0.mca");
        var previous = Payload(version: 1, length: 9_000);

        await using (var file = await OpenAsync(path))
        {
            await WriteAsync(file, 3, 4, previous);
            await file.FlushAsync();
        }

        // The new version, as big as the previous one, is written in full but fails before the header switches to it,
        // as a crash would.
        await using (var file = await OpenAsync(path, payloadWritten: (_, _) => throw new IOException("Crashed.")))
        {
            await Assert.ThrowsAsync<IOException>(() => WriteAsync(file, 3, 4, Payload(version: 2, length: 9_000)));
            Assert.Equal(previous, (await file.GetChunkBytesAsync(3, 4))!.Value.ToArray());
        }

        await using var reopened = await OpenAsync(path);
        Assert.Equal(previous, (await reopened.GetChunkBytesAsync(3, 4))!.Value.ToArray());
    }

    [Fact]
    public async Task RewritesReuseFreedSectors()
    {
        // Chunks alternately grown and shrunk, flushed like autosaves: once each size has its place, the file stops
        // growing.
        var path = Path.Join(this.folder, "r.0.0.mca");
        var lengths = new List<long>();
        await using (var file = await OpenAsync(path))
        {
            for (var round = 0; round < 12; round++)
            {
                for (var x = 0; x < 4; x++)
                    await WriteAsync(file, x, 0, Payload(round, round % 2 == 0 ? 1_000 : 11_000));

                await file.FlushAsync();
                lengths.Add(file.EndOfFile);
            }
        }

        Assert.Single(lengths.Skip(4).Distinct());
        Assert.Equal(lengths[^1], new FileInfo(path).Length);

        // A chunk moves into the file's last free run when nothing else is free: one sector after the header, one for
        // each of two chunks, the second rewritten twice so the sector after it is the only free one.
        var lastRun = Path.Join(this.folder, "r.1.0.mca");
        await using var region = await OpenAsync(lastRun);
        await WriteAsync(region, 0, 0, Payload(1, 100));
        await WriteAsync(region, 1, 0, Payload(1, 100));
        await region.FlushAsync();
        await WriteAsync(region, 1, 0, Payload(2, 100));
        await region.FlushAsync();
        await WriteAsync(region, 1, 0, Payload(3, 100));
        await region.FlushAsync();
        Assert.Equal(5 * SectorSize, region.EndOfFile);

        await WriteAsync(region, 0, 0, Payload(4, 100));
        Assert.Equal(5 * SectorSize, region.EndOfFile);
    }

    [Fact]
    public async Task ConcurrentSavesAndLoadsStayOrdered()
    {
        // Versions of each chunk reserved in order, as saves take their snapshots, then compressed and written in any
        // order, while readers load the chunks: every read is a whole version, never older than one read before.
        const int versions = 30;
        const int chunks = 4;
        var path = Path.Join(this.folder, "r.0.0.mca");
        await using (var file = await OpenAsync(path))
        {
            var writes = new List<Task>();
            for (var version = 1; version <= versions; version++)
            {
                for (var x = 0; x < chunks; x++)
                {
                    var turn = file.ReserveWrite(x, 0);
                    var payload = Payload(version, Random.Shared.Next(100, 20_000));
                    var chunkX = x;
                    writes.Add(Task.Run(async () =>
                    {
                        using (turn)
                        {
                            await Task.Delay(Random.Shared.Next(3));
                            var compressed = file.Compress(payload);
                            try
                            {
                                await turn.TurnAsync;
                                await file.WriteChunkAsync(chunkX, 0, compressed.Written);
                            }
                            finally
                            {
                                compressed.Return();
                            }
                        }
                    }));
                }
            }

            var written = Task.WhenAll(writes);
            var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                var seen = new int[chunks];
                while (!written.IsCompleted)
                {
                    for (var x = 0; x < chunks; x++)
                    {
                        if (await file.GetChunkBytesAsync(x, 0) is not Memory<byte> bytes)
                            continue;

                        var version = VersionOf(bytes.Span);
                        Assert.True(version >= seen[x], $"Chunk {x} went back from version {seen[x]} to {version}.");
                        seen[x] = version;
                    }
                }
            }));

            await Task.WhenAll(readers.Append(written));
        }

        await using var reopened = await OpenAsync(path);
        for (var x = 0; x < chunks; x++)
            Assert.Equal(versions, VersionOf((await reopened.GetChunkBytesAsync(x, 0))!.Value.Span));

        // A region saving a chunk in an autosave and as it unloads, with a change between them, which the chunk then
        // loads with: the unload's save is written last.
        var regionFolder = Path.Join(this.folder, "world");
        await using var region = new Region(0, 0, regionFolder);
        await region.InitAsync();
        region.SetChunk(Complete(0, 0));
        for (var round = 0; round < 10; round++)
        {
            var chunk = await region.GetChunkAsync(0, 0);
            var block = round % 2 == 0 ? BlocksRegistry.Stone : BlocksRegistry.Dirt;
            chunk.SetBlock(1, 64, 1, block);

            var flush = region.WriteChangesAsync();
            chunk.SetBlock(2, 64, 2, block);
            await Task.WhenAll(flush, region.UnloadChunk(0, 0));

            var reloaded = await region.GetChunkAsync(0, 0);
            Assert.Equal(block, reloaded.GetBlock(1, 64, 1));
            Assert.Equal(block, reloaded.GetBlock(2, 64, 2));
        }
    }

    private static async Task<RegionFile> OpenAsync(string path, Func<int, int, ValueTask> payloadWritten = null)
    {
        var file = new RegionFile(path, NbtCompression.ZLib) { PayloadWritten = payloadWritten };
        await file.InitializeAsync();
        return file;
    }

    // Writes uncompressed bytes as a chunk, compressed like the region's saves.
    private static async Task WriteAsync(RegionFile file, int x, int z, byte[] uncompressed)
    {
        var compressed = file.Compress(uncompressed);
        try
        {
            await file.SetChunkAsync(x, z, compressed.Written.ToArray());
        }
        finally
        {
            compressed.Return();
        }
    }

    // Reads a region file as vanilla lays it out, without RegionFile: whole sectors, runs after the header that don't
    // overlap, each starting with its length and zlib compression type, or a stub flagged external whose payload is in
    // the chunk's c.x.z.mcc file.
    private static Dictionary<(int X, int Z), byte[]> ReadVanillaLayout(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(0, bytes.Length % SectorSize);

        var used = new bool[bytes.Length / SectorSize];
        used[0] = true;
        used[1] = true;

        var chunks = new Dictionary<(int X, int Z), byte[]>();
        for (var index = 0; index < 1024; index++)
        {
            var location = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(index * 4));
            if (location == 0)
                continue;

            var (sector, count) = (location >> 8, location & 0xFF);
            Assert.InRange(sector, 2, used.Length - count);
            for (var occupied = sector; occupied < sector + count; occupied++)
            {
                Assert.False(used[occupied], $"Sector {occupied} is in two chunks.");
                used[occupied] = true;
            }

            var offset = sector * SectorSize;
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset));
            var type = bytes[offset + 4];
            Assert.InRange(length, 1, count * SectorSize - 4);
            Assert.Equal(2, type & 0x7F);

            var position = (index % 32, index / 32);
            var external = Path.Join(Path.GetDirectoryName(path), $"c.{position.Item1}.{position.Item2}.mcc");
            var compressed = bytes[(offset + 5)..(offset + 4 + length)];
            if ((type & 0x80) != 0)
            {
                Assert.Equal(1, length);
                compressed = File.ReadAllBytes(external);
            }

            chunks[position] = Inflate(compressed);
        }

        return chunks;
    }

    private static byte[] Inflate(byte[] compressed)
    {
        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress))
            zlib.CopyTo(inflated);

        return inflated.ToArray();
    }

    // The chunk's NBT with a byte array of random (incompressible) filler added to its root compound, which ends with
    // its end tag.
    private static byte[] WithFiller(byte[] chunk, int length, int seed)
    {
        if (length == 0)
            return chunk;

        var name = Encoding.UTF8.GetBytes("obsidian_test_filler");
        var tag = new byte[1 + 2 + name.Length + 4 + length];
        tag[0] = (byte)NbtTagType.ByteArray;
        BinaryPrimitives.WriteUInt16BigEndian(tag.AsSpan(1), (ushort)name.Length);
        name.CopyTo(tag, 3);
        BinaryPrimitives.WriteInt32BigEndian(tag.AsSpan(3 + name.Length), length);
        new Random(seed).NextBytes(tag.AsSpan(7 + name.Length));

        return [.. chunk[..^1], .. tag, .. chunk[^1..]];
    }

    // Bytes standing for a chunk's NBT: its version, then random filler, so its compressed size follows its length.
    private static byte[] Payload(int version, int length)
    {
        var payload = new byte[Math.Max(length, sizeof(int))];
        new Random(version * 31 + length).NextBytes(payload);
        BinaryPrimitives.WriteInt32BigEndian(payload, version);

        return payload;
    }

    private static int VersionOf(ReadOnlySpan<byte> payload) => BinaryPrimitives.ReadInt32BigEndian(payload);

    private static Chunk Complete(int x, int z)
    {
        var chunk = new Chunk(x, z);
        chunk.SetChunkStatus(ChunkGenStage.full);
        return chunk;
    }

    private static string FixtureRegion()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (; directory is not null; directory = directory.Parent)
        {
            var path = Path.Join(directory.FullName, "Obsidian.Tests", "Assets", "vanilla-26.3-world", "dimensions",
                "minecraft", "overworld", "region", "r.0.0.mca");
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException("Cannot find the vanilla 26.3 fixture's region in the source tree.");
    }
}
