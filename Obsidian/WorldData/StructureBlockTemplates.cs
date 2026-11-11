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
    private sealed record Entry(StructureTemplate? Template);

    private static readonly ConditionalWeakTable<World, StructureBlockTemplates> worlds = new();

    private readonly ConcurrentDictionary<string, Entry> cache = new(StringComparer.Ordinal);
    private readonly object writesLock = new();
    private Task pendingWrites = Task.CompletedTask;
    private readonly string generated;

    private StructureBlockTemplates(World world) => this.generated = Path.Combine(world.WorldFolderPath, "generated");

    internal static StructureBlockTemplates For(AbstractLevel level)
    {
        var world = level as World ?? (World)((Dimension)level).ParentWorld;
        return worlds.GetValue(world, static world => new StructureBlockTemplates(world));
    }

    internal string Folder => this.generated;

    internal void Remove(string name) => this.cache.TryRemove(name, out _);

    internal StructureTemplate? Cached(string name) => this.cache.GetValueOrDefault(name)?.Template;

    internal void Put(string name, StructureTemplate template) => this.cache[name] = new Entry(template);

    internal Task<StructureTemplate?> ReadAsync(string name)
    {
        if (this.cache.TryGetValue(name, out var cached))
            return Task.FromResult(cached.Template);

        return Task.Run(() =>
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
                // Vanilla tries resource templates after an unreadable generated file.
            }

            if (template is null)
            {
                template = PluginStructures.CurrentRegistry.Templates.GetValueOrDefault(name)
                    ?? StructureRegistry.GetCurrent(name);
                if (template.Size == Vector.Zero)
                    template = null;
            }

            // A redstone save may have published a newer snapshot while the worker read the disk.
            return this.cache.GetOrAdd(name, new Entry(template)).Template;
        });
    }

    /// <summary>
    /// Reserves a write in snapshot order, before releasing the tick lock. The chain continues after IO failures,
    /// and no world lock is held when it writes or waits for the previous file.
    /// </summary>
    internal Task<bool> WriteAsync(string name, StructureTemplate template)
    {
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
        var split = name.IndexOf(':');
        var resource = name[(split + 1)..];
        // Identifiers may contain dots, but file paths must stay inside generated/<namespace>/structure.
        var ns = name[..split];
        if (ns is "." or ".."
            || resource.Length > 0 && resource.Split('/').Any(part => part is "" or "." or ".."))
            throw new ArgumentException("Invalid structure path", nameof(name));

        return Path.Combine(this.generated, ns, "structure", resource.Replace('/', Path.DirectorySeparatorChar)
            + ".nbt");
    }
}
