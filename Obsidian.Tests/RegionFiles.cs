using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.ChunkData;
using Obsidian.Entities;
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
using System.Threading;
using System.Threading.Tasks;
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Region files stay in vanilla's layout however Obsidian rewrites them, commit writes in an order a crash can't
/// corrupt, keep a chunk's previous version when a write doesn't finish, reuse the sectors chunks leave, and write each
/// chunk's versions in order under concurrent saves, unloads and closing.
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
    public async Task CommitsFlushPayloadsBeforeEntriesAndEntriesBeforeFreeing()
    {
        // Two rewrites committed together: both payloads, a flush, both header entries, a flush, then the previous
        // versions' sectors.
        var path = Path.Join(this.folder, "r.0.0.mca");
        await using (var file = await OpenAsync(path))
        {
            await WriteAsync(file, 0, 0, Payload(1, 5_000));
            await WriteAsync(file, 1, 0, Payload(1, 5_000));

            var steps = Record(file);
            await StageAsync(file, 0, 0, Payload(2, 9_000));
            await StageAsync(file, 1, 0, Payload(2, 9_000));
            await file.CommitAsync();

            Assert.Equal(
            [
                new(RegionFile.Step.PayloadWritten, 0, 0),
                new(RegionFile.Step.PayloadWritten, 1, 0),
                new(RegionFile.Step.Flushed, -1, -1),
                new(RegionFile.Step.HeaderWriting, 0, 0),
                new(RegionFile.Step.HeaderWriting, 1, 0),
                new(RegionFile.Step.Flushed, -1, -1),
                new(RegionFile.Step.SectorsReleased, -1, -1)
            ], steps);
        }

        // An autosave of 64 changed chunks commits them together: two flushes of the chunk file.
        await using var region = new Region(0, 0, Path.Join(this.folder, "world"));
        await region.InitAsync();
        var chunks = Enumerable.Range(0, 64).Select(index => Complete(index % 8, index / 8)).ToArray();
        foreach (var chunk in chunks)
            region.SetChunk(chunk);

        await region.FlushAsync();
        foreach (var chunk in chunks)
            chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone);

        var autosave = Record(region.ChunkFile);
        await region.FlushAsync();

        Assert.Equal(64, autosave.Count(step => step.Step == RegionFile.Step.HeaderWriting));
        Assert.Equal(2, autosave.Count(step => step.Step == RegionFile.Step.Flushed));
    }

    [Fact]
    public async Task RewritesReuseFreedSectors()
    {
        // Chunks alternately grown and shrunk, each write committed: once each size has its place, the file stops
        // growing.
        var path = Path.Join(this.folder, "r.0.0.mca");
        var lengths = new List<long>();
        await using (var file = await OpenAsync(path))
        {
            for (var round = 0; round < 12; round++)
            {
                for (var x = 0; x < 4; x++)
                    await WriteAsync(file, x, 0, Payload(round, round % 2 == 0 ? 1_000 : 11_000));

                lengths.Add(file.EndOfFile);
            }
        }

        // Stable over the second half, and never more than the header and two versions of each chunk's bigger size.
        Assert.Single(lengths.Skip(6).Distinct());
        Assert.True(lengths[^1] <= (2 + 2 * 4 * 3) * SectorSize, $"The file grew to {lengths[^1]} bytes.");
        Assert.Equal(lengths[^1], new FileInfo(path).Length);

        // A chunk moves into the file's last free run when nothing else is free: one sector after the header, one for
        // each of two chunks, the second rewritten twice so the sector after it is the only free one.
        var lastRun = Path.Join(this.folder, "r.1.0.mca");
        await using var region = await OpenAsync(lastRun);
        await WriteAsync(region, 0, 0, Payload(1, 100));
        await WriteAsync(region, 1, 0, Payload(1, 100));
        await WriteAsync(region, 1, 0, Payload(2, 100));
        await WriteAsync(region, 1, 0, Payload(3, 100));
        Assert.Equal(5 * SectorSize, region.EndOfFile);

        await WriteAsync(region, 0, 0, Payload(4, 100));
        Assert.Equal(5 * SectorSize, region.EndOfFile);
    }

    [Fact]
    public async Task ConcurrentSavesAndLoadsStayOrdered()
    {
        // Versions of each chunk reserved in order, as saves take their snapshots, then compressed, staged and
        // committed in any order while readers load the chunks: every read is a whole version, never older than one
        // read before. The commits free previous versions' sectors under the reads, which later versions reuse.
        const int versions = 30;
        const int chunks = 4;
        var path = Path.Join(this.folder, "r.0.0.mca");
        await using (var file = await OpenAsync(path))
        {
            using var stop = new CancellationTokenSource();
            var reads = new int[4];
            var reading = reads.Select(_ => new TaskCompletionSource()).ToArray();
            var readers = Enumerable.Range(0, reads.Length).Select(reader => Task.Run(async () =>
            {
                var seen = new int[chunks];
                while (!stop.IsCancellationRequested)
                {
                    for (var x = 0; x < chunks; x++)
                    {
                        if (await file.GetChunkBytesAsync(x, 0) is not Memory<byte> bytes)
                            continue;

                        var version = VersionOf(bytes.Span);
                        Assert.True(version >= seen[x], $"Chunk {x} went back from version {seen[x]} to {version}.");
                        seen[x] = version;
                        reads[reader]++;
                    }

                    reading[reader].TrySetResult();
                }
            })).ToArray();

            await WriteAsync(file, 0, 0, Payload(0, 100));
            await Task.WhenAll(reading.Select(started => started.Task));

            var writes = new List<Task>();
            long sectorsWritten = 0;
            for (var version = 1; version <= versions; version++)
            {
                for (var x = 0; x < chunks; x++)
                {
                    var turn = file.ReserveWrite(x, 0);
                    var payload = Payload(version, Random.Shared.Next(100, 20_000));
                    Interlocked.Add(ref sectorsWritten, (payload.Length + 5 + SectorSize - 1) / SectorSize);

                    writes.Add(Task.Run(async () =>
                    {
                        await Task.Delay(Random.Shared.Next(3));
                        var compressed = file.Compress(payload);
                        try
                        {
                            await file.StageAsync(turn, compressed.Written);
                        }
                        finally
                        {
                            compressed.Return();
                            turn.Dispose();
                        }

                        await file.CommitAsync();
                        await turn.Done;
                        Assert.Null(turn.Failure);
                    }));
                }
            }

            await Task.WhenAll(writes);
            stop.Cancel();
            await Task.WhenAll(readers);

            Assert.All(reads, count => Assert.True(count > 0));
            Assert.True(file.EndOfFile < sectorsWritten * SectorSize / 2, "Freed sectors weren't reused.");
        }

        await using var reopened = await OpenAsync(path);
        for (var x = 0; x < chunks; x++)
            Assert.Equal(versions, VersionOf((await reopened.GetChunkBytesAsync(x, 0))!.Value.Span));

        // A region saving a chunk in an autosave and as it unloads, which the chunk then loads with: with a change
        // between them the unload's save is written last, and without one the unload waits for the autosave's.
        var regionFolder = Path.Join(this.folder, "world");
        await using var region = new Region(0, 0, regionFolder);
        await region.InitAsync();
        region.SetChunk(Complete(0, 0));
        for (var round = 0; round < 10; round++)
        {
            var chunk = await region.GetChunkAsync(0, 0);
            var block = round % 2 == 0 ? BlocksRegistry.Stone : BlocksRegistry.Dirt;
            var changedBetween = round % 4 < 2;
            chunk.SetBlock(1, 64, 1, block);

            var flush = region.WriteChangesAsync();
            if (changedBetween)
                chunk.SetBlock(2, 64, 2, block);

            await Task.WhenAll(flush, region.UnloadChunk(0, 0));

            var reloaded = await region.GetChunkAsync(0, 0);
            Assert.Equal(block, reloaded.GetBlock(1, 64, 1));
            if (changedBetween)
                Assert.Equal(block, reloaded.GetBlock(2, 64, 2));
        }
    }

    [Fact]
    public async Task FailuresKeepEveryChunkReadable()
    {
        (string Name, Func<string, Task> Check)[] cases =
        [
            ("payload write interrupted", path => InterruptedWriteAsync(path, RegionFile.Step.PayloadWritten)),
            ("header write interrupted", path => InterruptedWriteAsync(path, RegionFile.Step.HeaderWriting)),
            ("external chunk rewritten in another codec, interrupted", ExternalRewriteKeepsItsCodecAsync),
            ("entries sharing sectors", SharedSectorsStayWithTheirOtherChunkAsync),
            ("reservation given up between two writes", GivenUpReservationKeepsOrderAsync),
            ("commit and its cleanup failing before the entries",
                path => FailedCleanupEndsEveryWriteAsync(path, RegionFile.Step.Flushed)),
            ("commit and its cleanup failing at an entry",
                path => FailedCleanupEndsEveryWriteAsync(path, RegionFile.Step.HeaderWriting))
        ];

        await FixtureCases.AssertAllAsync(cases, testCase => testCase.Name,
            testCase => testCase.Check(this.RegionPath(testCase.Name)));
    }

    [Fact]
    public async Task ExternalFilesKeepOnlyTheCommittedVersion()
    {
        (string Name, Func<string, Task> Check)[] cases =
        [
            ("stale file's deferred deletion ending as a new file moves in", DeferredDeletionLeavesTheNewFileAsync),
            ("temporary failing once written", FailedTemporaryGoesWithItsWriteAsync),
            ("temporaries left by a crash", LeftTemporariesAreReclaimedAsync)
        ];

        await FixtureCases.AssertAllAsync(cases, testCase => testCase.Name,
            testCase => testCase.Check(this.RegionPath(testCase.Name)));
    }

    [Fact]
    public async Task SaveFailingInBothFilesRetriesBoth()
    {
        // An item saved with its chunk, then picked up, and the chunk's next save failing in both files: the save
        // after it still writes the chunk's entities, without the item, so the item isn't there once reloaded.
        var regionFolder = Path.Join(this.folder, "world");
        await using (var region = new Region(0, 0, regionFolder))
        {
            await region.InitAsync();
            var chunk = Complete(0, 0);
            region.SetChunk(chunk);
            var item = new ItemEntity
            {
                Level = null!,
                EntityId = 1,
                Position = new VectorF(8.5f, 65f, 8.5f),
                Item = new ItemStack(ItemsRegistry.Apple, 3)
            };
            region.TryAddEntity(item);
            await region.FlushAsync();

            region.TryRemoveEntity(item);
            chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone);
            region.ChunkFile.Observe = FailAt(RegionFile.Step.Flushed);
            region.EntityFile.Observe = FailAt(RegionFile.Step.Flushed);

            await Assert.ThrowsAsync<AggregateException>(() => region.FlushAsync());

            region.ChunkFile.Observe = null;
            region.EntityFile.Observe = null;
            await region.FlushAsync();
        }

        await using var reloaded = new Region(0, 0, regionFolder);
        await reloaded.InitAsync();
        var reloadedChunk = (Chunk)await reloaded.GetChunkAsync(0, 0);

        Assert.Equal(BlocksRegistry.Stone, reloadedChunk.GetBlock(1, 64, 1));
        Assert.Empty(reloadedChunk.PendingEntities);
    }

    [Fact]
    public async Task RegionSavesWaitForTheWritesBeforeThem()
    {
        (string Name, Func<string, Task> Check)[] cases =
        [
            ("unload with nothing new during an autosave", UnloadWaitsForTheAutosaveAsync),
            ("closing during an admitted save", ClosingWaitsForAdmittedSavesAsync),
            ("snapshot failing after retiring block entities", FailedSnapshotReleasesWhatItRetiredAsync)
        ];

        await FixtureCases.AssertAllAsync(cases, testCase => testCase.Name,
            testCase => testCase.Check(Path.GetDirectoryName(this.RegionPath(testCase.Name))!));
    }

    // A write failing where a crash would, after its payload is written or as its header entry is: the chunk reads as
    // its previous version, then and once the file is opened again.
    private static async Task InterruptedWriteAsync(string path, RegionFile.Step failingAt)
    {
        var previous = Payload(1, 9_000);
        await using (var file = await OpenAsync(path))
            await WriteAsync(file, 3, 4, previous);

        await using (var file = await OpenAsync(path))
        {
            file.Observe = FailAt(failingAt);

            // As big as the previous version, which used to be overwritten in place.
            await Assert.ThrowsAsync<IOException>(() => WriteAsync(file, 3, 4, Payload(2, 9_000)));
            Assert.Equal(previous, (await file.GetChunkBytesAsync(3, 4))!.Value.ToArray());
        }

        await using var reopened = await OpenAsync(path);
        Assert.Equal(previous, (await reopened.GetChunkBytesAsync(3, 4))!.Value.ToArray());
    }

    // An external gzip chunk rewritten by a zlib file, failing as its header entry is written: the file it moved in
    // is the commit point of a chunk staying external, and keeps the codec the stub names, so the chunk reads whole.
    private static async Task ExternalRewriteKeepsItsCodecAsync(string path)
    {
        await using (var gzip = new RegionFile(path, NbtCompression.GZip))
        {
            await gzip.InitializeAsync();
            await WriteAsync(gzip, 2, 2, Payload(1, 1_300_000));
        }

        var rewritten = Payload(2, 1_300_000);
        await using (var file = await OpenAsync(path))
        {
            file.Observe = FailAt(RegionFile.Step.HeaderWriting);

            await Assert.ThrowsAsync<IOException>(() => WriteAsync(file, 2, 2, rewritten));
            Assert.Equal(rewritten, (await file.GetChunkBytesAsync(2, 2))!.Value.ToArray());
        }

        await using var reopened = await OpenAsync(path);
        Assert.Equal(rewritten, (await reopened.GetChunkBytesAsync(2, 2))!.Value.ToArray());
    }

    // Two header entries pointing at one sector, as a damaged file can have: rewriting one doesn't free the sector
    // the other still uses, so a later write can't take it.
    private static async Task SharedSectorsStayWithTheirOtherChunkAsync(string path)
    {
        var shared = Payload(1, 500);
        await using (var file = await OpenAsync(path))
            await WriteAsync(file, 0, 0, shared);

        var bytes = await File.ReadAllBytesAsync(path);
        bytes.AsSpan(0, 4).CopyTo(bytes.AsSpan(4));
        await File.WriteAllBytesAsync(path, bytes);

        await using var damaged = await OpenAsync(path);
        Assert.Equal(2, damaged.OverlappingChunks);

        await WriteAsync(damaged, 0, 0, Payload(2, 500));
        await WriteAsync(damaged, 2, 0, Payload(3, 500));
        Assert.Equal(shared, (await damaged.GetChunkBytesAsync(1, 0))!.Value.ToArray());
    }

    // Three writes of a chunk reserved in turn, the middle one given up while the first is still pending: the third
    // waits for the first too, so it can't be overwritten by it.
    private static async Task GivenUpReservationKeepsOrderAsync(string path)
    {
        await using var file = await OpenAsync(path);
        var first = file.ReserveWrite(0, 0);
        var givenUp = file.ReserveWrite(0, 0);
        var third = file.ReserveWrite(0, 0);

        givenUp.Dispose();
        Assert.False(third.TurnAsync.IsCompleted);

        await StageAsync(file, first, Payload(1, 100));
        await file.CommitAsync();
        await third.TurnAsync;

        await StageAsync(file, third, Payload(3, 100));
        await file.CommitAsync();
        Assert.Equal(3, VersionOf((await file.GetChunkBytesAsync(0, 0))!.Value.Span));
    }

    // An internal, an external and another internal rewrite committed together, the commit failing at a step and
    // deleting the external one's temporary failing too: every write still ends, with the commit's failure, and the
    // chunks keep their previous versions and take their next writes. Opening the file again reclaims the temporary.
    private static async Task FailedCleanupEndsEveryWriteAsync(string path, RegionFile.Step failingAt)
    {
        var previous = Payload(1, 100);
        var next = Payload(3, 100);
        await using (var file = await OpenAsync(path))
        {
            for (var x = 0; x < 3; x++)
                await WriteAsync(file, x, 0, previous);

            file.Observe = FailAt(failingAt, RegionFile.Step.TemporaryDiscarding);
            RegionFile.ChunkWrite[] writes =
            [
                await StageAsync(file, 0, 0, Payload(2, 100)),
                await StageAsync(file, 1, 0, Payload(2, 1_300_000)),
                await StageAsync(file, 2, 0, Payload(2, 100))
            ];

            var failure = await Assert.ThrowsAsync<AggregateException>(file.CommitAsync);
            await Task.WhenAll(writes.Select(write => write.Done)).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.All(writes, write => Assert.Same(failure.InnerExceptions[0], write.Failure));

            file.Observe = null;
            for (var x = 0; x < 3; x++)
                Assert.Equal(previous, (await file.GetChunkBytesAsync(x, 0))!.Value.ToArray());

            await WriteAsync(file, 1, 0, next);
            Assert.Equal(next, (await file.GetChunkBytesAsync(1, 0))!.Value.ToArray());
        }

        Assert.Single(Temporaries(path));

        await (await OpenAsync(path)).DisposeAsync();
        Assert.Empty(Temporaries(path));
    }

    // An external chunk rewritten internal while a read of another chunk runs, which defers deleting its stale
    // external file, then external again; the read ends once the new file is moved in, before its entry switches. The
    // deferred deletion leaves the new file, which the chunk then reads.
    private static async Task DeferredDeletionLeavesTheNewFileAsync(string path)
    {
        await using var file = await OpenAsync(path);
        await WriteAsync(file, 0, 0, Payload(1, 100));
        await WriteAsync(file, 2, 2, Payload(1, 1_300_000));

        // The read keeps running until resumed.
        var read = new Pause();
        file.Observe = step => step is { Step: RegionFile.Step.Reading, ChunkX: 0 }
            ? read.WaitAsync()
            : ValueTask.CompletedTask;

        var reading = file.GetChunkBytesAsync(0, 0);
        await read.Paused;
        await WriteAsync(file, 2, 2, Payload(2, 100));

        // External again, paused once its file is moved in; the read ends meanwhile.
        var moveIn = new Pause();
        file.Observe = step => step is { Step: RegionFile.Step.HeaderWriting, ChunkX: 2 }
            ? moveIn.WaitAsync()
            : ValueTask.CompletedTask;

        var rewritten = Payload(3, 1_300_000);
        var writing = WriteAsync(file, 2, 2, rewritten);
        await moveIn.Paused;

        read.Resume();
        await reading;

        moveIn.Resume();
        await writing;

        Assert.Equal(rewritten, (await file.GetChunkBytesAsync(2, 2))!.Value.ToArray());
    }

    // An external rewrite whose temporary fails once written and flushed: the temporary goes with the write, and the
    // chunk keeps its previous version.
    private static async Task FailedTemporaryGoesWithItsWriteAsync(string path)
    {
        var previous = Payload(1, 100);
        await using var file = await OpenAsync(path);
        await WriteAsync(file, 2, 2, previous);
        file.Observe = FailAt(RegionFile.Step.TemporaryWritten);

        await Assert.ThrowsAsync<IOException>(() => WriteAsync(file, 2, 2, Payload(2, 1_300_000)));

        Assert.Empty(Temporaries(path));
        Assert.Equal(previous, (await file.GetChunkBytesAsync(2, 2))!.Value.ToArray());
    }

    // Temporaries a crash left beside the region file: opening it deletes those of its own chunks' external files,
    // and leaves other regions' and files that aren't temporaries.
    private static async Task LeftTemporariesAreReclaimedAsync(string path)
    {
        string[] own = [TemporaryName(2, 2), TemporaryName(31, 0)];
        string[] others = [TemporaryName(-1, 2), TemporaryName(32, 0), "c.2.2.mcc.notes.tmp"];

        var folder = Path.GetDirectoryName(path)!;
        foreach (var name in own.Concat(others))
            await File.WriteAllBytesAsync(Path.Join(folder, name), [1, 2, 3]);

        await (await OpenAsync(path)).DisposeAsync();

        Assert.Equal(others.Order(), Temporaries(path).Order());
    }

    // An autosave takes the chunk's change and pauses before staging it; the chunk then unloads with nothing new to
    // write, so its own commit has nothing of the chunk's. The unload waits for the autosave's write, so the chunk
    // reloads with the change.
    private static async Task UnloadWaitsForTheAutosaveAsync(string folder)
    {
        await using var region = new Region(0, 0, folder);
        await region.InitAsync();
        var chunk = Complete(0, 0);
        region.SetChunk(chunk);
        await region.FlushAsync();

        chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone);
        var (paused, resume) = PauseAt(region.ChunkFile, RegionFile.Step.PayloadWritten);
        var autosave = region.FlushAsync();
        await paused;

        var unload = region.UnloadChunk(0, 0);
        try
        {
            await AssertWaitsAsync(unload);
        }
        finally
        {
            resume();
        }

        await Task.WhenAll(autosave, unload);
        Assert.Equal(BlocksRegistry.Stone, (await region.GetChunkAsync(0, 0)).GetBlock(1, 64, 1));
    }

    // An unload paused while staging its chunk, then the region closing: closing admits no new save and waits for
    // the unload, whose chunk is then on the disk.
    private static async Task ClosingWaitsForAdmittedSavesAsync(string folder)
    {
        var region = new Region(0, 0, folder);
        await region.InitAsync();
        var chunk = Complete(0, 0);
        chunk.SetBlock(1, 64, 1, BlocksRegistry.Stone);
        region.SetChunk(chunk);

        var (paused, resume) = PauseAt(region.ChunkFile, RegionFile.Step.PayloadWritten);
        var unload = region.UnloadChunk(0, 0);
        await paused;

        var closing = region.DisposeAsync().AsTask();
        try
        {
            await AssertWaitsAsync(closing);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => region.FlushAsync());
        }
        finally
        {
            resume();
        }

        await Task.WhenAll(unload, closing);

        await using var reopened = new Region(0, 0, folder);
        await reopened.InitAsync();
        Assert.Equal(BlocksRegistry.Stone, (await reopened.GetChunkAsync(0, 0)).GetBlock(1, 64, 1));
    }

    // An unload whose snapshot fails after retiring the chunk's block entities still tells them their chunk unloaded.
    private static async Task FailedSnapshotReleasesWhatItRetiredAsync(string folder)
    {
        var unloaded = new List<BlockEntity>();
        var region = new Region(0, 0, folder)
        {
            BlockEntityUnloaded = unloaded.Add,
            SaveStructureStarts = (_, _, _) => throw new InvalidOperationException("The snapshot failed.")
        };

        await using (region)
        {
            await region.InitAsync();
            var chunk = Complete(0, 0);
            var crate = new Vector(4, 70, 4);
            chunk.SetBlockEntity(crate.X, crate.Y, crate.Z,
                new ContainerBlockEntity(new Container(9)) { Id = "probe:crate", BlockPosition = crate });
            region.SetChunk(chunk);

            await Assert.ThrowsAsync<InvalidOperationException>(() => region.UnloadChunk(0, 0));
            Assert.Single(unloaded);
        }
    }

    private string RegionPath(string testCase)
    {
        var caseFolder = Path.Join(this.folder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(caseFolder);

        return Path.Join(caseFolder, "r.0.0.mca");
    }

    private static List<RegionFile.Event> Record(RegionFile file)
    {
        var steps = new List<RegionFile.Event>();
        file.Observe = step =>
        {
            lock (steps)
                steps.Add(step);

            return ValueTask.CompletedTask;
        };

        return steps;
    }

    private static Func<RegionFile.Event, ValueTask> FailAt(params RegionFile.Step[] failingAt) => step =>
        failingAt.Contains(step.Step) ? ValueTask.FromException(new IOException("Crashed.")) : ValueTask.CompletedTask;

    // Pauses the file's writes at their first step of a kind; returns when one reached it, and how to resume.
    private static (Task Paused, Action Resume) PauseAt(RegionFile file, RegionFile.Step pausingAt)
    {
        var pause = new Pause();
        file.Observe = step => step.Step == pausingAt ? pause.WaitAsync() : ValueTask.CompletedTask;

        return (pause.Paused, pause.Resume);
    }

    // The names of the external files' temporaries beside a region file.
    private static IEnumerable<string> Temporaries(string path) =>
        Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Select(file => Path.GetFileName(file));

    private static string TemporaryName(int chunkX, int chunkZ) => $"c.{chunkX}.{chunkZ}.mcc.{Guid.NewGuid():N}.tmp";

    // The task can't end while the write it waits for is paused; the bound only gives a wrong ending time to show.
    private static async Task AssertWaitsAsync(Task waiting)
    {
        var ended = await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromMilliseconds(300)));
        Assert.NotSame(waiting, ended);
    }

    private static async Task<RegionFile> OpenAsync(string path)
    {
        var file = new RegionFile(path, NbtCompression.ZLib);
        await file.InitializeAsync();
        return file;
    }

    // Stages uncompressed bytes as a chunk, compressed like the region's saves, for the next commit.
    private static async Task<RegionFile.ChunkWrite> StageAsync(RegionFile file, int x, int z, byte[] uncompressed)
    {
        var compressed = file.Compress(uncompressed);
        try
        {
            return await file.StageAsync(x, z, compressed.Written);
        }
        finally
        {
            compressed.Return();
        }
    }

    private static async Task StageAsync(RegionFile file, RegionFile.ChunkWrite write, byte[] uncompressed)
    {
        var compressed = file.Compress(uncompressed);
        try
        {
            await file.StageAsync(write, compressed.Written);
        }
        finally
        {
            compressed.Return();
            write.Dispose();
        }
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

    // Holds the steps that wait for it until resumed; resumed, it lets every later one through.
    private sealed class Pause
    {
        private readonly TaskCompletionSource paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Completes once a step waits for the pause.
        public Task Paused => this.paused.Task;

        public ValueTask WaitAsync()
        {
            this.paused.TrySetResult();
            return new ValueTask(this.resumed.Task);
        }

        public void Resume() => this.resumed.TrySetResult();
    }
}
