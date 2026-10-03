using Obsidian.Entities;
using Obsidian.Entities.Factories;
using Obsidian.Nbt;
using System.IO;
using System.Threading;

namespace Obsidian.WorldData;

internal sealed class MobStorage(AbstractLevel level) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<long, RegionFile> files = [];
    private readonly HashSet<long> loadedChunks = [];
    private readonly Dictionary<long, List<NbtCompound>> unsupported = [];
    private readonly ConcurrentDictionary<long, List<NbtCompound>> pending = [];

    private async Task<RegionFile> GetFileAsync(int x, int z)
    {
        var rx = x >> 5;
        var rz = z >> 5;
        var key = NumericsHelper.IntsToLong(rx, rz);
        if (files.TryGetValue(key, out var file))
            return file;
        var folder = Path.Combine(level.FolderPath, "entities");
        Directory.CreateDirectory(folder);
        file = new RegionFile(Path.Combine(folder, $"r.{rx}.{rz}.mca"), NbtCompression.ZLib);
        await file.InitializeAsync();
        files.Add(key, file);
        return file;
    }

    internal async Task LoadChunkAsync(int x, int z)
    {
        await gate.WaitAsync();
        try
        {
            var key = NumericsHelper.IntsToLong(x, z);
            if (loadedChunks.Contains(key))
                return;
            var file = await GetFileAsync(x, z);
            var bytes = await file.GetChunkBytesAsync(NumericsHelper.Modulo(x, 32), NumericsHelper.Modulo(z, 32));
            List<NbtCompound> known = [];
            List<NbtCompound> unknown = [];
            if (bytes is { } data)
            {
                using var stream = new ReadOnlyStream(data);
                var reader = new NbtReader(stream);
                if (reader.ReadNextTag() is not NbtCompound root)
                    throw new InvalidDataException($"Invalid entity chunk {x}, {z}.");
                if (root.TryGetTag<NbtList>("Entities", out var entities))
                    foreach (var tag in entities.OfType<NbtCompound>())
                    {
                        if (TryGetType(tag, out var type) && EntitySpawner.CreateMob(level, type) != null)
                            known.Add(tag);
                        else
                            unknown.Add(tag);
                    }
            }
            unsupported[key] = unknown;
            loadedChunks.Add(key);
            pending[key] = known;
            level.EnqueueEntityAction(() =>
            {
                foreach (var tag in known)
                {
                    TryGetType(tag, out var type);
                    var mob = EntitySpawner.CreateMob(level, type)!;
                    mob.EntityId = Server.GetNextEntityId();
                    mob.ReadSave(tag);
                    if (!mob.Alive || level.Regions.Values.Any(region => region.Entities.Values.Any(entity => entity.Uuid == mob.Uuid)))
                        continue;
                    level.SpawnEntity(mob);
                }
                pending.TryRemove(key, out _);
                return default;
            });
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool TryGetType(NbtCompound tag, out EntityType type)
    {
        type = default;
        return tag.TryGetTagValue<string>("id", out var id) && id.StartsWith("minecraft:", StringComparison.Ordinal) &&
            Enum.TryParse(id[10..], true, out type);
    }

    internal async Task SaveAsync()
    {
        foreach (var mob in level.Regions.Values.SelectMany(region => region.Entities.Values).OfType<Mob>().Where(mob => mob.HasAi).ToArray())
        {
            var (x, z) = mob.Position.ToChunkCoord();
            await LoadChunkAsync(x, z);
        }
        await gate.WaitAsync();
        try
        {
            var mobs = level.Regions.Values.SelectMany(region => region.Entities.Values).OfType<Mob>()
                .Where(mob => mob.HasAi && mob.Alive && !mob.IsRemoved).DistinctBy(mob => mob.Uuid)
                .GroupBy(mob => { var (x, z) = mob.Position.ToChunkCoord(); return NumericsHelper.IntsToLong(x, z); })
                .ToDictionary(group => group.Key, group => group.ToArray());
            foreach (var key in loadedChunks.Union(mobs.Keys).ToArray())
            {
                NumericsHelper.LongToInts(key, out var x, out var z);
                var file = await GetFileAsync(x, z);
                var entities = mobs.GetValueOrDefault(key) ?? [];
                var unknown = unsupported.GetValueOrDefault(key) ?? [];
                var waiting = pending.GetValueOrDefault(key) ?? [];
                using var stream = new MemoryStream();
                await using (var writer = new NbtWriterStream(stream, NbtCompression.ZLib, ""))
                {
                    writer.WriteInt("DataVersion", 4671);
                    writer.WriteArray("Position", new[] { x, z });
                    writer.WriteListStart("Entities", NbtTagType.Compound, entities.Length + unknown.Count + waiting.Count);
                    foreach (var mob in entities)
                        mob.WriteSave(writer);
                    foreach (var tag in unknown)
                        writer.WriteTag(tag);
                    foreach (var tag in waiting)
                        writer.WriteTag(tag);
                    writer.EndList();
                    writer.EndCompound();
                    await writer.TryFinishAsync();
                }
                // Finish the compression trailer before copying the bytes into the region file.
                await file.SetChunkAsync(NumericsHelper.Modulo(x, 32), NumericsHelper.Modulo(z, 32), stream.ToArray());
            }
            foreach (var file in files.Values)
                file.Flush();
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var file in files.Values)
            await file.DisposeAsync();
        gate.Dispose();
    }
}
