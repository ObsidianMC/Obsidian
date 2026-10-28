using Obsidian.API.Data;
using Obsidian.Nbt;
using Obsidian.Plugins;
using System.IO;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// Lazy world/level files. Only MarkDirty runs a codec; saves write detached snapshots. The gate covers replacement
/// too, so an old save cannot finish over a reloaded instance. Unload flushes dirty NBT before removing live objects.
/// </summary>
internal sealed class SavedDataStorage(string directory)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);

    internal SavedData<TValue> GetOrCreate<TValue>(SavedDataType<TValue> type, Func<TValue> create)
    {
        using var call = type.Binding.Enter();
        lock (this.gate)
        {
            if (this.entries.TryGetValue(type.Identifier, out var existing)
                && ReferenceEquals(existing.Binding, type.Binding))
            {
                return (SavedData<TValue>)existing.Handle;
            }

            var path = this.FilePath(type.Identifier);
            TValue value;
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                var root = new NbtReader(stream, NbtCompression.GZip).ReadNextTag() as NbtCompound
                    ?? throw new InvalidDataException($"{path} isn't saved data.");
                value = type.Codec.Decode(root["data"]);
            }
            else
            {
                value = create();
            }

            var entry = new Entry(type.Binding);
            var handle = new SavedData<TValue>(value, () => type.Binding.Enter(), current =>
            {
                // Caller owns synchronization of the plugin object. Encode before taking the storage gate.
                var snapshot = AttachmentStorage.CopyValue("data", type.Codec.Encode(current));
                lock (this.gate)
                {
                    if (!ReferenceEquals(this.entries.GetValueOrDefault(type.Identifier), entry))
                        throw new InvalidOperationException("This saved data handle was replaced.");

                    entry.Snapshot = snapshot;
                    entry.Dirty = true;
                }
            });
            entry.Handle = handle;
            this.entries[type.Identifier] = entry;
            ((PluginContainer)type.Binding.Owner).TrackAddition(() =>
            {
                lock (this.gate)
                {
                    if (ReferenceEquals(this.entries.GetValueOrDefault(type.Identifier), entry))
                    {
                        // During unload, write the snapshot already published while the plugin was active. No
                        // codec runs here; later world saves leave the now-unregistered file untouched.
                        try
                        {
                            this.WriteEntry(type.Identifier, entry);
                        }
                        finally
                        {
                            this.entries.Remove(type.Identifier);
                        }
                    }
                }
            });

            if (!File.Exists(path))
                handle.MarkDirty();

            return handle;
        }
    }

    internal string FilePath(string identifier)
    {
        var parts = identifier.Split(':', 2);
        return Path.Combine(directory, parts[0], parts[1].Replace('/', Path.DirectorySeparatorChar) + ".dat");
    }

    internal void Save()
    {
        lock (this.gate)
        {
            foreach (var (identifier, entry) in this.entries)
            {
                if (!entry.Dirty || entry.Snapshot is null || !PluginDataBindings.IsCurrent(entry.Binding))
                    continue;

                using var call = entry.Binding.TryEnter();
                if (call is null)
                    continue;

                this.WriteEntry(identifier, entry);
            }
        }
    }

    // Called holding gate, including during unload. This uses only host-owned NBT and never plugin behavior.
    private void WriteEntry(string identifier, Entry entry)
    {
        if (!entry.Dirty || entry.Snapshot is null)
            return;

        var path = this.FilePath(identifier);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            using (var stream = File.Create(temporary))
            using (var writer = new NbtWriterStream(stream, NbtCompression.GZip, ""))
            {
                // Vanilla SavedData's gzip envelope: DataVersion and the payload under data.
                writer.WriteInt("DataVersion", ServerConstants.DataVersion);
                writer.WriteTag(entry.Snapshot);
                writer.EndCompound();
                writer.TryFinish();
            }

            File.Move(temporary, path, overwrite: true);
            entry.Dirty = false;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private sealed class Entry(PluginDataBinding binding)
    {
        internal PluginDataBinding Binding { get; } = binding;
        internal object Handle { get; set; } = null!;
        internal INbtTag? Snapshot { get; set; }
        internal bool Dirty { get; set; }
    }
}
