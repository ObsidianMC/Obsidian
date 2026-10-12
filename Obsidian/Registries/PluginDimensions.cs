using Microsoft.Extensions.Logging;
using Obsidian.Plugins;
using Obsidian.Registries.Codecs;
using Obsidian.WorldData.Generators.Mojang;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// Plugins' dimensions, from their level stem files like a data pack's: a file
/// <c>data/&lt;namespace&gt;/dimension/&lt;path&gt;.json</c> is the dimension <c>&lt;namespace&gt;:&lt;path&gt;</c>
/// (see <see cref="LevelStemData"/>), generated in the default world as a level of that name.
/// </summary>
/// <remarks>
/// A dimension added stays until the server stops, even once its plugin unloads, with the stem and noise settings it
/// was read with: changing a live level's generator would mix two generators' chunks. For the same reason a reload
/// changing a stem, or the plugin data it reads, keeps the dimension as it was, with a warning, until a restart.
/// A dimension whose generator is made of a plugin's types can't outlive the plugin's code, though: when the plugin
/// unloads, it's released (see <see cref="Release"/>) and its level stops generating new chunks. Once the plugin is
/// back, the dimension is read again from the stem it was added with (see <see cref="Apply"/>), and its level
/// generates again, unless its stem or the plugin data it reads changed: then it stays stopped until a restart.
/// Vanilla's dimensions can't be overridden, and when plugins define the same dimension, the first in load order wins.
/// </remarks>
internal static partial class PluginDimensions
{
    private const string Folder = "dimension/";

    private static readonly Lock applying = new();

    // Gson reads data packs leniently, so comments and trailing commas are fine.
    private static readonly JsonDocumentOptions documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static ImmutableArray<DimensionFile> added = [];

    // Added dimensions whose stem, or what it reads from plugins' data, changed since they were added: once stopped,
    // they stay stopped until a restart rather than being read again.
    private static ImmutableHashSet<string> restartRequired = [];

    /// <summary>
    /// The plugins' dimensions added since the server started, in the order they were added.
    /// </summary>
    public static ImmutableArray<MojangDimension> Added => [.. added.Select(file => file.Dimension)];

    /// <summary>
    /// Every dimension generated with vanilla's generator: vanilla's and plugins'.
    /// </summary>
    public static IEnumerable<MojangDimension> All => MojangDimension.Vanilla.Concat(Added);

    /// <summary>
    /// Finds the level stem files in a plugin's files, by their paths.
    /// </summary>
    /// <returns>Each file's path and dimension identifier; other files are left out.</returns>
    public static IEnumerable<(string Path, string Identifier)> FindFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var parts = path.Split('/', 3);
            if (parts.Length < 3
                || parts[0] != "data"
                || !path.EndsWith(".json", StringComparison.Ordinal)
                || parts[2].Length <= Folder.Length + ".json".Length
                || !parts[2].StartsWith(Folder, StringComparison.Ordinal))
            {
                continue;
            }

            yield return (path, $"{parts[1]}:{parts[2][Folder.Length..^".json".Length]}");
        }
    }

    /// <summary>
    /// Reads a level stem file. The dimension types, biomes, biome tags and noise settings it names must be
    /// registered.
    /// </summary>
    /// <param name="identifier">The dimension's identifier, like <c>ruby:crystal_world</c>.</param>
    /// <param name="source">Where the file comes from, for messages.</param>
    /// <param name="noise">The noise settings and worldgen types it can name; <see cref="PluginNoise.Current"/> by
    /// default.</param>
    /// <exception cref="InvalidDataException">
    /// The file isn't valid, saying where and what's expected, or it's for a dimension in the <c>minecraft</c>
    /// namespace, which plugins can't override.
    /// </exception>
    public static DimensionFile Parse(
        string identifier,
        string source,
        ReadOnlySpan<byte> json,
        WorldgenNoise? noise = null)
    {
        if (!PluginTags.IsFileIdentifier(identifier))
            throw new InvalidDataException($"{source}: {identifier} isn't a valid identifier.");

        if (identifier.StartsWith("minecraft:", StringComparison.Ordinal))
            throw new InvalidDataException($"{source} would override vanilla's {identifier}, which plugins can't.");

        noise ??= PluginNoise.Current;

        try
        {
            var bytes = json.ToArray();
            using var document = JsonDocument.Parse(bytes, documentOptions);
            var entries = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var dimension = LevelStemData.Read(identifier, document.RootElement, noise, entries);

            // A change to what it reads from plugins' data is a change to the dimension too: the noise files its noise
            // settings and biome source read, and the biomes it names, which tags may resolve differently.
            var biomes = string.Join(' ', dimension.PossibleBiomes.Select(biome => biome.Name));
            var text = $"{Encoding.UTF8.GetString(bytes)}\n{biomes}\n{noise.Sources(entries)}";

            return new DimensionFile(identifier, source, text, bytes, dimension);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException($"{source} isn't a valid dimension file: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Adds the files' dimensions that weren't added yet. Ones added before stay as they were, and those whose file
    /// changed are logged: the change applies after a restart. Added dimensions that were released, and didn't change,
    /// are read again from their stems with the plugins loaded now, so that their levels can generate again (see
    /// <see cref="WorldData.Generators.MojangGenerator.Resume"/>).
    /// </summary>
    /// <remarks>
    /// A file whose dimension isn't kept, because another plugin defines it first or the dimension was added before,
    /// is released (see <see cref="MojangDimension.Release"/>), so that it doesn't keep the plugins it's made of
    /// loaded. A released file that wasn't added isn't added: its plugin is reloaded to read it again.
    /// </remarks>
    /// <param name="files">The loaded plugins' files, in load order.</param>
    /// <returns>Whether a dimension was added.</returns>
    public static bool Apply(IEnumerable<DimensionFile> files, ILogger logger)
    {
        lock (applying)
        {
            var byIdentifier = new Dictionary<string, DimensionFile>();
            var current = added;
            var changed = false;

            foreach (var file in files)
            {
                if (byIdentifier.TryGetValue(file.Identifier, out var first))
                {
                    Log.Duplicate(logger, file.Identifier, file.Source, first.Source);
                    Discard(file);
                    continue;
                }

                byIdentifier.Add(file.Identifier, file);

                var existing = current.FirstOrDefault(entry => entry.Identifier == file.Identifier);
                if (ReferenceEquals(existing, file))
                    continue;

                if (existing is not null)
                {
                    // The same stem read again with the plugins loaded now replaces a released dimension.
                    if (existing.Json != file.Json)
                    {
                        Log.ChangedUntilRestart(logger, file.Identifier, file.Source);
                        restartRequired = restartRequired.Add(file.Identifier);
                    }
                    else if (existing.Dimension.IsReleased && !file.Dimension.IsReleased)
                    {
                        current = current.Replace(existing, file);
                        changed = true;
                        continue;
                    }

                    Discard(file);
                    continue;
                }

                if (file.Dimension.IsReleased)
                {
                    Log.ReleasedUntilReload(logger, file.Identifier, file.Source);
                    continue;
                }

                current = current.Add(file);
                changed = true;
            }

            // Released when a plugin they generate with unloaded, they're read again once it's back, unless they
            // changed.
            var released = current
                .Where(file => file.Dimension.IsReleased && !restartRequired.Contains(file.Identifier))
                .ToList();

            foreach (var file in released)
            {
                if (Rebuild(file, logger) is DimensionFile rebuilt)
                {
                    current = current.Replace(file, rebuilt);
                    changed = true;
                }
            }

            added = current;

            return changed;

            void Discard(DimensionFile file)
            {
                if (!current.Any(entry => ReferenceEquals(entry, file)) && !file.Dimension.IsReleased)
                    file.Dimension.Release();
            }
        }
    }

    /// <summary>
    /// The added dimension <paramref name="identifier"/>, released or not, or null.
    /// </summary>
    public static MojangDimension? Find(string identifier) =>
        added.FirstOrDefault(file => file.Identifier == identifier)?.Dimension;

    // Reads a released dimension's stem again, or returns null when it can't be: a plugin it generates with isn't
    // loaded, or the plugin data it reads changed, which applies after a restart.
    private static DimensionFile? Rebuild(DimensionFile file, ILogger logger)
    {
        DimensionFile rebuilt;

        try
        {
            rebuilt = Parse(file.Identifier, file.Source, file.Stem);
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Reading creates plugins' types, whose code may fail.
            Log.RebuildFailed(logger, ex, file.Identifier);
            return null;
        }

        if (rebuilt.Json == file.Json)
            return rebuilt;

        Log.ChangedUntilRestart(logger, file.Identifier, file.Source);
        rebuilt.Dimension.Release();
        restartRequired = restartRequired.Add(file.Identifier);

        return null;
    }

    /// <summary>
    /// Releases the dimensions whose generators are made of an unloading plugin's types (see
    /// <see cref="MojangDimension.Release"/>), so that they don't keep its code loaded: those added, and those of
    /// the files plugins hold that weren't.
    /// </summary>
    /// <param name="held">The files plugins hold, the unloading plugin's included.</param>
    /// <returns>The added dimensions released.</returns>
    public static IReadOnlyList<MojangDimension> Release(PluginContainer plugin, IEnumerable<DimensionFile> held)
    {
        lock (applying)
        {
            var released = added
                .Select(file => file.Dimension)
                .Where(dimension => !dimension.IsReleased && dimension.Plugins.Contains(plugin))
                .ToList();

            foreach (var dimension in released)
                dimension.Release();

            foreach (var file in held)
            {
                if (!file.Dimension.IsReleased && file.Dimension.Plugins.Contains(plugin))
                    file.Dimension.Release();
            }

            return released;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Left out {Identifier} from {Source}: {First} defined it first")]
        public static partial void Duplicate(ILogger logger, string identifier, string source, string first);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Kept dimension {Identifier} as it was: the change in {Source} applies after a restart")]
        public static partial void ChangedUntilRestart(ILogger logger, string identifier, string source);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Left out dimension {Identifier} from {Source}: a plugin it generates with unloaded since it "
                + "was read, so its plugin has to be reloaded to add it")]
        public static partial void ReleasedUntilReload(ILogger logger, string identifier, string source);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Dimension {Identifier} stays stopped: reading its level stem again failed")]
        public static partial void RebuildFailed(ILogger logger, Exception exception, string identifier);
    }
}

/// <summary>
/// A plugin's level stem file.
/// </summary>
/// <param name="Identifier">The dimension's identifier, like <c>ruby:crystal_world</c>.</param>
/// <param name="Source">Where the file comes from, for messages.</param>
/// <param name="Json">The file's text, with the biomes and plugin noise files it reads, to tell a changed dimension
/// on reload.</param>
/// <param name="Stem">The file's bytes, to read the dimension again once it's released.</param>
/// <param name="Dimension">What the stem generates.</param>
internal sealed record DimensionFile(
    string Identifier,
    string Source,
    string Json,
    byte[] Stem,
    MojangDimension Dimension);
