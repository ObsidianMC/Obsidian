using Obsidian.WorldData.Structures;
using System.IO;
using System.Runtime.CompilerServices;

namespace Obsidian.WorldData;

/// <summary>
/// A world's generated templates, shared across its dimensions. File reads and writes run on workers. Redstone
/// saves publish only to the in-memory cache; a powered corner evicts the cache, including a cached miss.
/// </summary>
internal sealed class StructureBlockTemplates
{
    // Generations need reference equality: two unloaded entries must not compare equal after eviction.
    private sealed class Entry(StructureTemplate? template, bool loaded = true)
    {
        public StructureTemplate? Template { get; } = template;

        public bool Loaded { get; } = loaded;
    }

    private static readonly ConditionalWeakTable<World, StructureBlockTemplates> worlds = new();

    private readonly ConcurrentDictionary<string, Entry> cache = new(StringComparer.Ordinal);
    private readonly object writesLock = new();
    private Task pendingWrites = Task.CompletedTask;
    private readonly string generated;
    private readonly Func<string, Task<StructureTemplate?>>? readSource;

    private StructureBlockTemplates(World world) : this(Path.Combine(world.WorldFolderPath, "generated"))
    {
    }

    /// <summary>A generated-file store, optionally supplied an asynchronous source reader.</summary>
    internal StructureBlockTemplates(string generated, Func<string, Task<StructureTemplate?>>? readSource = null)
    {
        this.generated = generated;
        this.readSource = readSource;
    }

    internal static StructureBlockTemplates For(AbstractLevel level)
    {
        var world = level as World ?? (World)((Dimension)level).ParentWorld;
        return worlds.GetValue(world, static world => new StructureBlockTemplates(world));
    }

    internal string Folder => this.generated;

    internal void Remove(string name) => this.cache.TryRemove(name, out _);

    internal StructureTemplate? Cached(string name) =>
        StructureTemplateNames.IsValid(name) ? this.cache.GetValueOrDefault(name)?.Template : null;

    internal void Put(string name, StructureTemplate template)
    {
        if (StructureTemplateNames.IsValid(name))
            this.cache[name] = new Entry(template);
    }

    internal Task<StructureTemplate?> ReadAsync(string name)
    {
        // A rejected generated path must never reach the resource fallback, even if no file was opened.
        if (!StructureTemplateNames.IsValid(name))
            return Task.FromResult<StructureTemplate?>(null);

        var entry = this.cache.GetOrAdd(name, static _ => new Entry(null, loaded: false));
        return entry.Loaded ? Task.FromResult(entry.Template) : this.ReadAndPublishAsync(name, entry);
    }

    private async Task<StructureTemplate?> ReadAndPublishAsync(string name, Entry expected)
    {
        var template = await (this.readSource?.Invoke(name) ?? Task.Run(() => this.ReadFromSources(name)))
            .ConfigureAwait(false);

        // Entry identity is the eviction generation. Remove or SAVE replaces it, so a pending worker cannot
        // repopulate an evicted name or overwrite a newer snapshot. Retry against the current generation.
        if (this.cache.TryUpdate(name, new Entry(template), expected))
            return template;

        return await this.ReadAsync(name).ConfigureAwait(false);
    }

    private StructureTemplate? ReadFromSources(string name)
    {
        StructureTemplate? template = null;
        try
        {
            var path = this.PathFor(name);
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                template = StructureTemplate.Load(stream);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Vanilla tries resource templates after an unreadable generated file, only for a valid name.
        }

        if (template is null)
        {
            template = PluginStructures.CurrentRegistry.Templates.GetValueOrDefault(name)
                ?? StructureRegistry.GetCurrent(name);
            if (template.Size == Vector.Zero)
                template = null;
        }

        return template;
    }

    /// <summary>
    /// Reserves a write in snapshot order, before releasing the tick lock. The chain continues after IO failures,
    /// and no world lock is held when it writes or waits for the previous file.
    /// </summary>
    internal Task<bool> WriteAsync(string name, StructureTemplate template)
    {
        if (!StructureTemplateNames.IsValid(name))
            return Task.FromResult(false);

        lock (this.writesLock)
        {
            var previous = this.pendingWrites;
            var write = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);
                try
                {
                    var path = this.PathFor(name);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var temporary = path + ".tmp";
                    using (var stream = File.Create(temporary))
                        template.Save(stream);

                    File.Move(temporary, path, overwrite: true);
                    return true;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                    or ArgumentException)
                {
                    return false;
                }
            });
            this.pendingWrites = write;
            return write;
        }
    }

    private string PathFor(string name)
    {
        if (!StructureTemplateNames.IsValid(name))
            throw new ArgumentException("Invalid structure path", nameof(name));

        var split = name.IndexOf(':');
        var ns = name[..split];
        var resource = name[(split + 1)..];
        return Path.Combine(this.generated, ns, "structure", resource.Replace('/', Path.DirectorySeparatorChar)
            + ".nbt");
    }
}
