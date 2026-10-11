using Microsoft.Extensions.Logging;
using System.Collections.Frozen;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// Merges plugins' tag files with vanilla's tags, the way NeoForge and Fabric merge mods' data packs: vanilla's tags
/// first, then each plugin's files in load order.
/// </summary>
/// <remarks>
/// A tag file is a data pack's (<c>data/&lt;namespace&gt;/tags/&lt;registry&gt;/&lt;path&gt;.json</c>): its
/// <c>values</c> add entries (identifiers, <c>#</c> tag references, or <c>{"id": ..., "required": false}</c>),
/// <c>replace</c> drops what came before it, and NeoForge's <c>remove</c> takes entries out. A tag whose required entry
/// is missing fails like in vanilla, but keeps vanilla's entries rather than disappearing, since server code holds
/// vanilla's tags. Block, item and entity type tags are supported so far.
/// </remarks>
internal static partial class PluginTags
{
    /// <summary>
    /// The registries plugins' tags may be for.
    /// </summary>
    public static FrozenSet<string> Registries { get; } = FrozenSet.Create("block", "item", "entity_type");

    private static readonly Lock applying = new();

    /// <summary>
    /// How many times plugins' tags were applied, so a client can tell it has older tags.
    /// </summary>
    public static int Revision => Volatile.Read(ref revision);

    private static int revision;

    // Vanilla's entries of the tags plugins changed, to restore when they no longer do.
    // By reference: a tag is a record, whose equality and hash change with its entries.
    private static readonly Dictionary<Tag, ImmutableArray<int>> vanillaEntries =
        new(ReferenceEqualityComparer.Instance);

    private static readonly Lazy<List<TagFile>> vanillaFiles = new(LoadVanillaFiles);

    private static readonly Lazy<FrozenDictionary<string, int>> vanillaBlockIds = new(() => BlocksRegistry.ResourceIds
        .Select((identifier, id) => (identifier, id))
        .ToFrozenDictionary(entry => entry.identifier, entry => entry.id));

    /// <summary>
    /// Reads a tag file.
    /// </summary>
    /// <param name="registry">The tag's registry, like <c>block</c>.</param>
    /// <param name="identifier">The tag's identifier, like <c>minecraft:mineable/pickaxe</c>.</param>
    /// <param name="source">Where the file comes from, for messages.</param>
    /// <exception cref="InvalidDataException">The file isn't a valid tag file.</exception>
    public static TagFile Parse(string registry, string identifier, string source, ReadOnlySpan<byte> json)
    {
        // Clients read the tag's identifier with vanilla's rules, and refuse the tags if it breaks them.
        if (!IsIdentifier(identifier))
            throw new InvalidDataException($"{source}: {identifier} isn't a valid tag identifier.");

        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            var root = document.RootElement;

            var replace = root.TryGetProperty("replace", out var replaceElement) && replaceElement.GetBoolean();
            var values = ReadEntries(root.GetProperty("values"));
            var remove = root.TryGetProperty("remove", out var removeElement) ? ReadEntries(removeElement) : [];

            return new TagFile(registry, identifier, source, replace, values, remove);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidDataException($"{source} isn't a valid tag file: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// The tag file that adds <paramref name="entries"/> (identifiers, or tags' after a <c>#</c>) to a tag.
    /// </summary>
    /// <exception cref="ArgumentException">The registry isn't supported, or an identifier isn't valid.</exception>
    public static TagFile FromEntries(string registry, string identifier, string source, IEnumerable<string> entries)
    {
        if (!Registries.Contains(registry))
            throw new ArgumentException($"Tags of {registry} aren't supported.", nameof(registry));

        var values = entries
            .Select(entry => entry.StartsWith('#')
                ? new TagFileEntry(Qualify(entry[1..]), IsTag: true, Required: true)
                : new TagFileEntry(Qualify(entry), IsTag: false, Required: true))
            .ToImmutableArray();

        foreach (var id in values.Select(value => value.Id).Prepend(Qualify(identifier)))
        {
            if (!IsIdentifier(id))
                throw new ArgumentException($"{id} isn't a valid identifier.", nameof(entries));
        }

        return new TagFile(registry, Qualify(identifier), source, Replace: false, values, Remove: []);
    }

    // Vanilla's identifier rules.
    private static bool IsIdentifier(string identifier)
    {
        var colon = identifier.IndexOf(':');

        return colon > 0
            && colon < identifier.Length - 1
            && identifier[..colon].All(c => IsPathChar(c) && c != '/')
            && identifier[(colon + 1)..].All(IsPathChar);

        static bool IsPathChar(char c) =>
            char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or '-' or '.' or '/';
    }

    /// <summary>
    /// Finds the tag files in a plugin's files, by their paths: <c>data/&lt;namespace&gt;/tags/&lt;registry&gt;/</c>
    /// followed by the tag's path and <c>.json</c>.
    /// </summary>
    /// <returns>Each file's path, registry and tag identifier; files of other registries are left out.</returns>
    public static IEnumerable<(string Path, string Registry, string Identifier)> FindFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var parts = path.Split('/');
            if (parts.Length < 5
                || parts[0] != "data"
                || parts[2] != "tags"
                || !Registries.Contains(parts[3])
                || !path.EndsWith(".json", StringComparison.Ordinal))
            {
                continue;
            }

            var tagPath = string.Join('/', parts[4..])[..^".json".Length];
            yield return (path, parts[3], $"{parts[1]}:{tagPath}");
        }
    }

    /// <summary>
    /// Makes the tags hold what vanilla's definitions and the given tag files add and remove, in order, like vanilla
    /// builds tags from its data packs. Applying no files restores vanilla's tags.
    /// </summary>
    public static void Apply(IEnumerable<TagFile> files, ILogger logger)
    {
        lock (applying)
        {
            var pluginFiles = files.ToList();

            if (pluginFiles.Count == 0)
            {
                foreach (var (tag, entries) in vanillaEntries)
                    tag.SetEntries(entries);

                TagsRegistry.SetPluginTags(new Dictionary<string, Tag[]>());
                Interlocked.Increment(ref revision);
                return;
            }

            // Vanilla's definitions come first, like vanilla's data pack, so that plugins' files add to or replace
            // them, and so that a change to a vanilla tag reaches the vanilla tags that refer to it.
            var byTag = vanillaFiles.Value
                .Concat(pluginFiles)
                .GroupBy(file => (file.Registry, file.Identifier))
                .ToDictionary(group => group.Key, group => group.ToList());

            var resolved = new Dictionary<(string Registry, string Identifier), ImmutableArray<int>?>();
            var pluginTags = new Dictionary<string, List<Tag>>();

            // Like vanilla's tag loader, tags are built after the tags they refer to.
            foreach (var key in SortByDependencies(byTag, logger))
                resolved[key] = Build(key, byTag[key], resolved, logger);

            foreach (var key in byTag.Keys)
            {
                var entries = resolved.GetValueOrDefault(key);

                if (TagsRegistry.TryGetVanilla(key.Registry, key.Identifier, out var vanilla))
                {
                    // A vanilla tag's own entries, from before any plugin changed it, which it keeps when it fails.
                    if (!vanillaEntries.TryGetValue(vanilla, out var original))
                        vanillaEntries[vanilla] = original = vanilla.Entries;

                    vanilla.SetEntries(entries ?? original);
                }
                else if (entries is ImmutableArray<int> merged)
                {
                    if (!pluginTags.TryGetValue(key.Registry, out var tags))
                        pluginTags[key.Registry] = tags = [];

                    tags.Add(CreateTag(key.Registry, key.Identifier, merged));
                }
            }

            TagsRegistry.SetPluginTags(pluginTags.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray()));
            Interlocked.Increment(ref revision);
        }
    }

    // The order to build tags in, each after the tags it requires, like vanilla's DependencySorter: a reference that
    // is optional or a removal follows the tag it names unless that would close a cycle, in which case the tag isn't
    // there yet when it's built. Tags whose required references form a cycle are left out, and fail. Tags are taken in
    // identifier order, so which optional reference of a cycle is left out doesn't depend on the order plugins' files
    // were read; vanilla's order comes from its hash map instead, so such cycles may resolve differently than there.
    private static List<(string Registry, string Identifier)> SortByDependencies(
        Dictionary<(string Registry, string Identifier), List<TagFile>> files,
        ILogger logger)
    {
        var keys = files.Keys
            .OrderBy(key => key.Registry, StringComparer.Ordinal)
            .ThenBy(key => key.Identifier, StringComparer.Ordinal)
            .ToList();

        var required = files.Keys.ToDictionary(key => key, _ => new HashSet<(string Registry, string Identifier)>());
        var optional = files.Keys.ToDictionary(key => key, _ => new HashSet<(string Registry, string Identifier)>());

        foreach (var (key, tagFiles) in files)
        {
            foreach (var entry in EffectiveFiles(tagFiles).SelectMany(file => file.Values))
            {
                if (entry.IsTag && entry.Required && files.ContainsKey((key.Registry, entry.Id)))
                    required[key].Add((key.Registry, entry.Id));
            }
        }

        foreach (var key in keys)
        {
            var references = EffectiveFiles(files[key])
                .SelectMany(file => file.Values.Where(entry => !entry.Required).Concat(file.Remove))
                .Where(entry => entry.IsTag)
                .Select(entry => (key.Registry, entry.Id));

            foreach (var reference in references)
            {
                if (files.ContainsKey(reference) && !Reaches(reference, key))
                    optional[key].Add(reference);
            }
        }

        var order = new List<(string Registry, string Identifier)>();
        var done = new Dictionary<(string Registry, string Identifier), bool>();
        var visiting = new HashSet<(string Registry, string Identifier)>();

        foreach (var key in keys)
            Visit(key);

        return order;

        // Whether the tag can be built: none of its required references are in a cycle.
        bool Visit((string Registry, string Identifier) key)
        {
            if (done.TryGetValue(key, out var ok))
                return ok;

            if (!visiting.Add(key))
            {
                Log.TagCycle(logger, key.Identifier);
                return false;
            }

            ok = true;
            foreach (var dependency in required[key])
                ok &= Visit(dependency);

            foreach (var dependency in optional[key])
                Visit(dependency);

            visiting.Remove(key);
            done[key] = ok;

            if (ok)
                order.Add(key);

            return ok;
        }

        // Whether a tag refers, through the references kept so far, to another.
        bool Reaches((string Registry, string Identifier) from, (string Registry, string Identifier) to)
        {
            var seen = new HashSet<(string Registry, string Identifier)>();
            var pending = new Stack<(string Registry, string Identifier)>([from]);

            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (current == to)
                    return true;

                if (!seen.Add(current))
                    continue;

                foreach (var next in required[current].Concat(optional[current]))
                    pending.Push(next);
            }

            return false;
        }
    }

    // Like vanilla's tag loader, a file that replaces the tag drops every file before it, failing ones too.
    private static IEnumerable<TagFile> EffectiveFiles(List<TagFile> tagFiles) =>
        tagFiles.Skip(Math.Max(tagFiles.FindLastIndex(file => file.Replace), 0));

    // A tag's entries, from the tags built before it, or null when it fails: a required entry or tag is missing.
    private static ImmutableArray<int>? Build(
        (string Registry, string Identifier) key,
        List<TagFile> tagFiles,
        Dictionary<(string Registry, string Identifier), ImmutableArray<int>?> resolved,
        ILogger logger)
    {
        // Like vanilla's tag builder: the entries in the order they're first added.
        var ids = new List<int>();
        var seen = new HashSet<int>();
        var failed = false;

        foreach (var file in EffectiveFiles(tagFiles))
        {
            foreach (var entry in file.Values)
            {
                if (Find(entry, file) is IEnumerable<int> found)
                {
                    foreach (var id in found)
                    {
                        if (seen.Add(id))
                            ids.Add(id);
                    }
                }
            }

            // NeoForge's removals never fail: a missing entry has nothing to remove.
            foreach (var entry in file.Remove)
            {
                if (Find(entry with { Required = false }, file) is IEnumerable<int> found)
                {
                    foreach (var id in found)
                    {
                        if (seen.Remove(id))
                            ids.Remove(id);
                    }
                }
            }
        }

        return failed ? null : [.. ids];

        IEnumerable<int>? Find(TagFileEntry entry, TagFile file)
        {
            IEnumerable<int>? found = entry.IsTag
                ? resolved.GetValueOrDefault((key.Registry, entry.Id))
                : FindId(key.Registry, entry.Id) is int id ? [id] : null;

            if (found is null && entry.Required)
            {
                Log.MissingEntry(logger, entry.IsTag ? $"#{entry.Id}" : entry.Id, key.Identifier, file.Source);
                failed = true;
            }

            return found;
        }
    }

    // Vanilla's block and item tag definitions, with their references to other tags, from the tags of vanilla's data
    // pack (Assets/tags.json, keyed by registry and path).
    private static List<TagFile> LoadVanillaFiles()
    {
        using var stream = typeof(PluginTags).Assembly.GetManifestResourceStream("Obsidian.Assets.tags.json")
            ?? throw new InvalidOperationException("Missing tags asset.");
        using var document = JsonDocument.Parse(stream);

        var files = new List<TagFile>();

        foreach (var tag in document.RootElement.EnumerateObject())
        {
            var registry = tag.Name[..tag.Name.IndexOf('/')];
            if (!Registries.Contains(registry))
                continue;

            var identifier = $"minecraft:{tag.Name[(registry.Length + 1)..]}";
            var values = ReadEntries(tag.Value.GetProperty("values"));

            files.Add(new TagFile(registry, identifier, "vanilla", Replace: false, values, Remove: []));
        }

        return files;
    }

    // The id of a registry's entry by its identifier.
    private static int? FindId(string registry, string identifier)
    {
        if (registry == "entity_type")
        {
            if (Entities.EntityNbt.TryParseType(identifier, out var vanilla))
                return (int)vanilla;

            return EntityDefinitionsRegistry.TryGet(identifier, out var definition) ? definition.Id : null;
        }

        if (registry == "block")
        {
            if (vanillaBlockIds.Value.TryGetValue(identifier, out var block))
                return block;

            return BlocksRegistry.TryGetPluginBlock(identifier, out var type) ? type.Id : null;
        }

        return ItemsRegistry.TryGet(identifier, out var item) ? item.Id : null;
    }

    // A new tag: its type is the registry followed by the folders of its path, like vanilla's generated tags.
    private static Tag CreateTag(string registry, string identifier, ImmutableArray<int> entries)
    {
        var colon = identifier.IndexOf(':');
        var path = identifier[(colon + 1)..];
        var folder = path.LastIndexOf('/');

        return new Tag
        {
            Namespace = identifier[..colon],
            Name = folder < 0 ? path : path[(folder + 1)..],
            Type = folder < 0 ? registry : $"{registry}/{path[..folder]}",
            Entries = entries
        };
    }

    private static ImmutableArray<TagFileEntry> ReadEntries(JsonElement array) =>
    [
        .. array.EnumerateArray().Select(element =>
        {
            var (id, required) = element.ValueKind == JsonValueKind.String
                ? (element.GetString(), true)
                : (element.GetProperty("id").GetString(),
                    !element.TryGetProperty("required", out var requiredElement) || requiredElement.GetBoolean());

            if (id is null)
                throw new InvalidDataException("An entry has no id.");

            // Like vanilla, a malformed identifier rejects the whole file, even in an optional entry.
            var isTag = id.StartsWith('#');
            var qualified = Qualify(isTag ? id[1..] : id);

            return IsIdentifier(qualified)
                ? new TagFileEntry(qualified, isTag, required)
                : throw new InvalidDataException($"{id} isn't a valid identifier.");
        })
    ];

    // Like vanilla's identifiers, one without a namespace, or with an empty one (:stone), is in minecraft's.
    private static string Qualify(string identifier) => identifier.IndexOf(':') switch
    {
        < 0 => $"minecraft:{identifier}",
        0 => $"minecraft{identifier}",
        _ => identifier
    };

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error,
            Message = "Tag {Tag} keeps its earlier entries: {Entry}, required by {Source}, doesn't exist")]
        public static partial void MissingEntry(ILogger logger, string entry, string tag, string source);

        [LoggerMessage(Level = LogLevel.Error, Message = "Tag {Tag} refers back to itself")]
        public static partial void TagCycle(ILogger logger, string tag);
    }
}

/// <summary>
/// A plugin's tag file, or the entries it added to a tag programmatically.
/// </summary>
/// <param name="Registry">The tag's registry, like <c>block</c>.</param>
/// <param name="Identifier">The tag's identifier, like <c>minecraft:mineable/pickaxe</c>.</param>
/// <param name="Source">Where the file comes from, for messages.</param>
/// <param name="Replace">Whether the file drops the entries the tag had before it.</param>
/// <param name="Values">The entries the file adds.</param>
/// <param name="Remove">The entries the file removes, NeoForge's extension.</param>
internal sealed record TagFile(
    string Registry,
    string Identifier,
    string Source,
    bool Replace,
    ImmutableArray<TagFileEntry> Values,
    ImmutableArray<TagFileEntry> Remove);

/// <param name="Id">An entry's identifier, or a tag's without its <c>#</c>.</param>
/// <param name="IsTag">Whether the entry is another tag, whose entries it stands for.</param>
/// <param name="Required">Whether the tag fails when the entry doesn't exist.</param>
internal readonly record struct TagFileEntry(string Id, bool IsTag, bool Required);
