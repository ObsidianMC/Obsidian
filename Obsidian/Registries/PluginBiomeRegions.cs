using Microsoft.Extensions.Logging;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.Registries.Codecs;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Regions;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// Plugins' biome regions, from their <c>data/&lt;namespace&gt;/biome_region/&lt;path&gt;.json</c> files (see
/// <see cref="BiomeRegionData"/>): alternative biome layouts that several plugins add to the same dimension, such as
/// the overworld, without overriding each other (see <see cref="RegionBiomeSource"/>).
/// </summary>
/// <remarks>
/// Regions are part of a dimension's generator: a dimension's regions are fixed when its biome source is first
/// created, as its first level starts generating, and stay until the server stops, so that a level's chunks all
/// generate with the same layout. Until then, they're the loaded plugins' regions, which feature ordering uses too.
/// A plugin loading, reloading or unloading later changes nothing in dimensions already generating, with a warning:
/// the change applies after a restart. A region whose plugin unloads keeps generating with its biomes, which stay
/// registered. Regions are data alone, so they never hold a plugin's code.
/// </remarks>
internal static partial class PluginBiomeRegions
{
    private const string Folder = "biome_region/";

    private static readonly Lock applying = new();

    // Gson reads data packs leniently, so comments and trailing commas are fine.
    private static readonly JsonDocumentOptions documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    // The loaded plugins' regions by dimension, in the order of their identifiers.
    private static ImmutableDictionary<string, RegionSet> loaded = ImmutableDictionary<string, RegionSet>.Empty;

    // The regions of the dimensions whose biome sources were created, for the server's life.
    private static ImmutableDictionary<string, RegionSet> fixedRegions = ImmutableDictionary<string, RegionSet>.Empty;

    /// <summary>
    /// Finds the biome region files in a plugin's files, by their paths.
    /// </summary>
    /// <returns>Each file's path and region identifier; other files are left out.</returns>
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
    /// Reads a biome region file. The biomes it names must be registered.
    /// </summary>
    /// <param name="identifier">The region's identifier, like <c>ruby:crystal_plains</c>.</param>
    /// <param name="source">Where the file comes from, for messages.</param>
    /// <exception cref="InvalidDataException">The file isn't valid, saying where and what's expected.</exception>
    public static BiomeRegionFile Parse(string identifier, string source, ReadOnlySpan<byte> json)
    {
        if (!PluginTags.IsFileIdentifier(identifier))
            throw new InvalidDataException($"{source}: {identifier} isn't a valid identifier.");

        try
        {
            var bytes = json.ToArray();
            using var document = JsonDocument.Parse(bytes, documentOptions);
            var region = BiomeRegionData.Read(identifier, document.RootElement);

            return new BiomeRegionFile(identifier, source, Encoding.UTF8.GetString(bytes), region);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException($"{source} isn't a valid biome region file: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Makes the files' regions the loaded ones, which dimensions not generating yet will generate with. Dimensions
    /// generating already keep theirs: a change to them is logged, and applies after a restart.
    /// </summary>
    /// <param name="files">The loaded plugins' region files, in load order; when two define the same region, the
    /// last wins, like any other entry.</param>
    public static void Apply(IEnumerable<BiomeRegionFile> files, ILogger logger)
    {
        lock (applying)
        {
            var byIdentifier = new Dictionary<string, BiomeRegionFile>();

            foreach (var file in files)
            {
                if (byIdentifier.TryGetValue(file.Identifier, out var earlier))
                    Log.Replaced(logger, file.Identifier, earlier.Source, file.Source);

                byIdentifier[file.Identifier] = file;
            }

            var previous = loaded;

            loaded = byIdentifier.Values
                .GroupBy(file => file.Region.Dimension)
                .ToImmutableDictionary(group => group.Key, group => new RegionSet(group));

            var dimensions = loaded.Keys.Concat(previous.Keys).Distinct();

            foreach (var dimension in dimensions)
            {
                var current = Fingerprint(loaded, dimension);

                if (fixedRegions.TryGetValue(dimension, out var fixedSet)
                    && current != fixedSet.Fingerprint
                    && current != Fingerprint(previous, dimension))
                {
                    Log.ChangedUntilRestart(logger, dimension);
                }
            }
        }
    }

    /// <summary>
    /// Wraps a dimension's biome source factory so that the sources it creates generate the dimension's regions.
    /// Creating one fixes the dimension's regions until the server stops.
    /// </summary>
    /// <param name="dimension">The dimension's identifier, its level stem's.</param>
    public static Func<RandomState, IClimateBiomeSource> Wrap(
        string dimension,
        Func<RandomState, IClimateBiomeSource> create) =>
        randomState => RegionBiomeSource.Create(dimension, create(randomState), Fix(dimension), randomState);

    /// <summary>
    /// A dimension's possible biomes with those of its regions: its fixed ones once it generates, or else the loaded
    /// plugins' ones.
    /// </summary>
    /// <param name="dimension">The dimension's identifier, its level stem's.</param>
    /// <param name="biomes">The possible biomes of the dimension's own biome source.</param>
    public static IReadOnlyList<BiomeCodec> PossibleBiomes(string dimension, IReadOnlyList<BiomeCodec> biomes)
    {
        var regions = fixedRegions.TryGetValue(dimension, out var fixedSet)
            ? fixedSet
            : loaded.GetValueOrDefault(dimension);

        return RegionBiomeSource.GetPossibleBiomes(biomes, regions?.Regions ?? []);
    }

    /// <summary>
    /// Lets go of a dimension's fixed regions, as no level generates it: building its generator failed (see
    /// <see cref="PluginDimensions.GiveBack"/>). The next biome source created for it takes the loaded plugins' ones.
    /// </summary>
    /// <param name="dimension">The dimension's identifier, its level stem's.</param>
    public static void Unfix(string dimension)
    {
        lock (applying)
            fixedRegions = fixedRegions.Remove(dimension);
    }

    private static ImmutableArray<BiomeRegion> Fix(string dimension)
    {
        lock (applying)
        {
            if (!fixedRegions.TryGetValue(dimension, out var fixedSet))
            {
                fixedSet = loaded.GetValueOrDefault(dimension) ?? RegionSet.Empty;
                fixedRegions = fixedRegions.Add(dimension, fixedSet);
            }

            return fixedSet.Regions;
        }
    }

    private static string Fingerprint(ImmutableDictionary<string, RegionSet> sets, string dimension) =>
        sets.GetValueOrDefault(dimension)?.Fingerprint ?? "";

    // A dimension's regions, ordered by identifier so that their order, which decides where each one generates,
    // doesn't depend on the order plugins load in, and their files' text, to tell when they changed.
    private sealed class RegionSet
    {
        public static RegionSet Empty { get; } = new([]);

        public ImmutableArray<BiomeRegion> Regions { get; }

        public string Fingerprint { get; }

        public RegionSet(IEnumerable<BiomeRegionFile> files)
        {
            var ordered = files.OrderBy(file => file.Identifier, StringComparer.Ordinal).ToList();

            this.Regions = [.. ordered.Select(file => file.Region)];
            this.Fingerprint = string.Join('\n', ordered.Select(file => $"{file.Identifier}\n{file.Json}"));
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Biome region {Identifier} from {Earlier} is replaced by {Source}")]
        public static partial void Replaced(ILogger logger, string identifier, string earlier, string source);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "The biome regions of {Dimension} changed: it keeps generating with the regions it started "
                + "with, and the change applies after a restart")]
        public static partial void ChangedUntilRestart(ILogger logger, string dimension);
    }
}

/// <summary>
/// A plugin's biome region file.
/// </summary>
/// <param name="Identifier">The region's identifier, like <c>ruby:crystal_plains</c>.</param>
/// <param name="Source">Where the file comes from, for messages.</param>
/// <param name="Json">The file's text, to tell a changed region.</param>
/// <param name="Region">The region the file defines.</param>
internal sealed record BiomeRegionFile(string Identifier, string Source, string Json, BiomeRegion Region);
