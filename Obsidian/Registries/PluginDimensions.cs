using Microsoft.Extensions.Logging;
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
/// A dimension added stays until the server stops, even once its plugin unloads: its level only uses vanilla's
/// generator and data already in the server's registries, and changing a live level's generator would corrupt it.
/// For the same reason a reload changing a stem keeps the dimension as it was, with a warning, until a restart.
/// Vanilla's dimensions can't be overridden, and when plugins define the same dimension, the first in load order
/// wins.
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
    /// Reads a level stem file. The dimension types, biomes and biome tags it names must be registered.
    /// </summary>
    /// <param name="identifier">The dimension's identifier, like <c>ruby:crystal_world</c>.</param>
    /// <param name="source">Where the file comes from, for messages.</param>
    /// <exception cref="InvalidDataException">
    /// The file isn't valid, saying where and what's expected, or it's for a dimension in the <c>minecraft</c>
    /// namespace, which plugins can't override.
    /// </exception>
    public static DimensionFile Parse(string identifier, string source, ReadOnlySpan<byte> json)
    {
        if (!PluginTags.IsIdentifier(identifier))
            throw new InvalidDataException($"{source}: {identifier} isn't a valid identifier.");

        if (identifier.StartsWith("minecraft:", StringComparison.Ordinal))
            throw new InvalidDataException($"{source} would override vanilla's {identifier}, which plugins can't.");

        try
        {
            var bytes = json.ToArray();
            using var document = JsonDocument.Parse(bytes, documentOptions);
            var dimension = LevelStemData.Read(identifier, document.RootElement);

            return new DimensionFile(identifier, source, Encoding.UTF8.GetString(bytes), dimension);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException($"{source} isn't a valid dimension file: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Adds the files' dimensions that weren't added yet. Ones added before stay as they were, and those whose file
    /// changed are logged: the change applies after a restart.
    /// </summary>
    /// <param name="files">The loaded plugins' files, in load order.</param>
    /// <returns>Whether a dimension was added.</returns>
    public static bool Apply(IEnumerable<DimensionFile> files, ILogger logger)
    {
        lock (applying)
        {
            var byIdentifier = new Dictionary<string, DimensionFile>();
            var current = added;

            foreach (var file in files)
            {
                if (byIdentifier.TryGetValue(file.Identifier, out var first))
                {
                    Log.Duplicate(logger, file.Identifier, file.Source, first.Source);
                    continue;
                }

                byIdentifier.Add(file.Identifier, file);

                var existing = current.FirstOrDefault(entry => entry.Identifier == file.Identifier);
                if (existing is null)
                    current = current.Add(file);
                else if (existing.Json != file.Json)
                    Log.ChangedUntilRestart(logger, file.Identifier, file.Source);
            }

            var changed = current.Length != added.Length;
            added = current;

            return changed;
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
    }
}

/// <summary>
/// A plugin's level stem file.
/// </summary>
/// <param name="Identifier">The dimension's identifier, like <c>ruby:crystal_world</c>.</param>
/// <param name="Source">Where the file comes from, for messages.</param>
/// <param name="Json">The file's text, to tell a changed file on reload.</param>
/// <param name="Dimension">What the stem generates.</param>
internal sealed record DimensionFile(string Identifier, string Source, string Json, MojangDimension Dimension);
