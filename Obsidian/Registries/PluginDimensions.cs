using Microsoft.Extensions.Logging;
using Obsidian.Plugins;
using Obsidian.Registries.Codecs;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Generators.Biomes;
using System.Collections.Frozen;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// Plugins' and data packs' dimensions, from their level stem files: a file
/// <c>data/&lt;namespace&gt;/dimension/&lt;path&gt;.json</c> is the dimension <c>&lt;namespace&gt;:&lt;path&gt;</c>
/// (see <see cref="LevelStemData"/>), generated in the default world as a level of that name, and vanilla's
/// dimensions as overridden.
/// </summary>
/// <remarks>
/// <para>
/// Until a level takes a dimension (see <see cref=Take/>), it follows the files, read again with the data they name
/// as it's overridden, so that at startup every stem sees every plugin's and data pack's data. A dimension a level
/// took stays until the server stops, even once its plugin unloads, with the stem and noise settings it was read
/// with: changing a live level's generator would mix two generators' chunks. For the same reason a reload changing
/// its stem, or the plugin data it reads, keeps the dimension as it was, with a warning, until a restart.
/// A dimension whose generator is made of a plugin's types can't outlive the plugin's code, though: when the plugin
/// unloads, it's released (see <see cref="Release"/>) and its level stops generating new chunks. Once the plugin is
/// back, the dimension is read again from the stem it was added with (see <see cref="Apply"/>), and its level
/// generates again, unless its stem or the plugin data it reads changed: then it stays stopped until a restart.
/// When files define the same dimension, the last in order wins.
/// </para>
/// <para>
/// Vanilla's dimensions (<see cref="WorldgenDimension.VanillaDimensions"/>) are read from their 26.3 stems and data,
/// with plugins' and data packs' overrides. Like vanilla, which reads a data pack's stems over those saved in level.dat, the
/// overrides apply to every world when it loads, and to the chunks generated from then on, and once a level took a
/// vanilla dimension, it's kept like a plugin's.
/// </para>
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

    // Vanilla's world preset's level stems, by dimension, with the parameter list each names: what they name
    // can be overridden, and they're read at runtime once it is.
    private static readonly FrozenDictionary<string, (string Stem, string? ParameterList)> vanillaStems =
        new Dictionary<string, (string Stem, string? ParameterList)>
        {
            ["minecraft:overworld"] = (NoiseStem("overworld", "overworld", "overworld"), "minecraft:overworld"),
            ["minecraft:the_nether"] = (NoiseStem("the_nether", "nether", "nether"), "minecraft:nether"),
            ["minecraft:the_end"] = (
                """
                {
                  "type": "minecraft:the_end",
                  "generator": {
                    "type": "minecraft:noise",
                    "settings": "minecraft:end",
                    "biome_source": { "type": "minecraft:the_end" }
                  }
                }
                """,
                null)
        }.ToFrozenDictionary();

    private static ImmutableArray<DimensionFile> added = [];

    // Added dimensions whose stem, or what it reads from plugins' data, changed since they were added: once stopped,
    // they stay stopped until a restart rather than being read again.
    private static ImmutableHashSet<string> restartRequired = [];

    // The dimensions levels generate with, which are kept from then on, with how many levels took each: one whose
    // level couldn't be created is given back (see GiveBack).
    private static ImmutableDictionary<string, int> taken = ImmutableDictionary<string, int>.Empty;

    /// <summary>
    /// The plugins' and data packs' dimensions added since the server started, in the order they were added; vanilla's
    /// overridden ones aren't.
    /// </summary>
    public static ImmutableArray<LevelStem> Added =>
        [.. added.Where(file => !IsVanilla(file.Identifier)).Select(file => file.Dimension)];

    /// <summary>
    /// Every dimension generated with vanilla's noise generator: vanilla's, as overridden, and plugins'.
    /// </summary>
    public static IEnumerable<WorldgenDimension> All =>
        WorldgenDimension.VanillaDimensions
            .Select(dimension => Find(dimension.Identifier) as WorldgenDimension ?? dimension)
            .Concat(Added.OfType<WorldgenDimension>());

    /// <summary>
    /// Whether a dimension is one of vanilla's world preset's.
    /// </summary>
    public static bool IsVanilla(string identifier) => vanillaStems.ContainsKey(identifier);

    /// <summary>
    /// Finds the level stem files in a plugin's or data pack's files, by their paths.
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
    /// <exception cref="InvalidDataException">The file isn't valid, saying where and what's expected.</exception>
    public static DimensionFile Parse(
        string identifier,
        string source,
        ReadOnlySpan<byte> json,
        WorldgenNoise? noise = null) =>
        Read(identifier, source, json.ToArray(), noise ?? PluginNoise.Current);

    // Reads a stem from bytes the file keeps, so that it can be read again.
    private static DimensionFile Read(string identifier, string source, byte[] bytes, WorldgenNoise noise)
    {
        if (!PluginTags.IsFileIdentifier(identifier))
            throw new InvalidDataException($"{source}: {identifier} isn't a valid identifier.");

        try
        {
            // The dimension type and biomes the stem defines inline are entries of the plugin's or data pack's,
            // read with its other files, which the stem names instead.
            var (stem, _) = LevelStemInline.Extract(identifier, bytes);
            using var document = JsonDocument.Parse(stem, documentOptions);
            var entries = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var dependencies = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var data = PluginWorldgen.Layer(WorldData.Generators.Density.WorldgenData.Vanilla,
                PluginCodecs.Applied, noise.Files).TraceReads((registry, id, json) =>
                    dependencies[$"{registry} {id}"] = json.GetRawText());
            var generator = document.RootElement.GetProperty("generator").GetProperty("type").GetString();
            var dimension = WorldData.Generators.Density.WorldgenData.Qualify(generator!) == "minecraft:noise"
                ? (LevelStem)PluginWorldgen.ReadDimension(identifier, stem, noise, data)
                : LevelStemData.Read(identifier, document.RootElement, noise, entries);

            // Obsidian creates vanilla's levels with vanilla's noise generator, which the overrides configure, and
            // with vanilla's dimension types; a stem can change those types' data.
            if (vanillaStems.ContainsKey(identifier) && dimension is not WorldgenDimension)
            {
                dimension.Release();

                throw CodecJson.Error(
                    "generator.type",
                    $"{identifier} must keep generating with minecraft:noise: Obsidian creates vanilla's levels with "
                    + "it");
            }

            if (vanillaStems.ContainsKey(identifier) && dimension.DimensionType.Name != VanillaType(identifier))
            {
                dimension.Release();

                throw CodecJson.Error(
                    "type",
                    $"{identifier} must keep its dimension type {VanillaType(identifier)}: override that type instead");
            }

            // A change to what it reads from plugins' data is a change to the dimension too: the noise files its noise
            // settings and biome source read, and the biomes it names, which tags may resolve differently.
            var biomes = string.Join(' ', dimension.SourceBiomes.Select(biome => biome.Name));
            data.StopTracing();
            var nativeSources = string.Join('\n', dependencies.Select(entry => $"{entry.Key}\n{entry.Value}"));
            var text = $"{Encoding.UTF8.GetString(bytes)}\n{biomes}\n{noise.Sources(entries)}\n{nativeSources}";

            return new DimensionFile(identifier, source, text, bytes, dimension);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException($"{source} isn't a valid dimension file: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Reads one of vanilla's dimensions from its stem and the current noise entries, including plugin overrides.
    /// </summary>
    /// <param name="identifier">One of vanilla's dimensions, like <c>minecraft:overworld</c>.</param>
    /// <param name="noise">The noise entries files define.</param>
    /// <exception cref="InvalidDataException">The overrides it names can't generate it.</exception>
    public static DimensionFile? ReadVanilla(string identifier, WorldgenNoise noise)
    {
        var (stem, _) = vanillaStems[identifier];
        return Parse(identifier, $"vanilla's {identifier}", Encoding.UTF8.GetBytes(stem), noise);
    }

    /// <summary>
    /// The dimension a level generates with, as files define it now: the added one of its identifier, or else the one
    /// given, like vanilla's compiled one. It's kept from then on: later changes apply after a restart, unless the
    /// level can't be created and gives it back (see <see cref="GiveBack"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The given dimension may be a stale snapshot of <see cref="Added"/>: a plugin unloading meanwhile may have
    /// released it, or a reload removed it (see <see cref="LevelStem.IsRemoved"/>), whether or not it holds plugins'
    /// code to release. Then, unless it's one of vanilla's, whose levels always exist, nothing is taken and this is
    /// null, so that the dimension can still follow its files and get a level once it's back. A snapshot a reload
    /// replaced takes its replacement. Nothing is taken either when its stem names another kind of generator since,
    /// which the level's generator can't generate.
    /// </para>
    /// <para>
    /// This is where vanilla's dimensions are built for their levels, so what changes how they generate as a whole,
    /// like a wrapper around their biome source, goes here or in <see cref="LevelStemData"/>.
    /// </para>
    /// </remarks>
    /// <returns>The dimension the level takes, whose dimension type it must use, or null when it's not
    /// available.</returns>
    public static LevelStem? Take(LevelStem dimension)
    {
        lock (applying)
        {
            var current = Find(dimension.Identifier) ?? dimension;
            var gone = (current.IsReleased || current.IsRemoved) && !IsVanilla(dimension.Identifier);

            // A dimension whose stem now names another kind of generator can't be generated by the level's.
            if (gone || current.GetType() != dimension.GetType())
                return null;

            taken = taken.SetItem(dimension.Identifier, taken.GetValueOrDefault(dimension.Identifier) + 1);
            return current;
        }
    }

    /// <summary>
    /// Gives back a dimension <see cref="Take"/> gave a level that couldn't be created, as building its generator
    /// failed before it generated anything (when its biomes' features can't be ordered, say). Once no level holds it,
    /// it follows its files again, so that a corrected stem can be taken, and its biome regions, which building its
    /// biome source fixed, are the loaded plugins' again (see <see cref="PluginBiomeRegions"/>).
    /// </summary>
    public static void GiveBack(string identifier)
    {
        lock (applying)
        {
            var holders = taken.GetValueOrDefault(identifier);
            if (holders > 1)
            {
                taken = taken.SetItem(identifier, holders - 1);
                return;
            }

            taken = taken.Remove(identifier);
            restartRequired = restartRequired.Remove(identifier);
            PluginBiomeRegions.Unfix(identifier);
        }
    }

    /// <summary>
    /// Makes the files' dimensions the added ones. Until a level takes a dimension (see <see cref="Take"/>), it follows
    /// the files: each winning stem is read again with the noise, types and biomes files define now, since what it
    /// names may have been overridden since it was read, and a dimension no file defines any more goes. Vanilla's
    /// follow the overrides of the noise they name too (see <see cref="ReadVanilla"/>), and are their compiled ones
    /// when nothing overrides them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A dimension a level took stays as it was, and a change to its stem, or to what the stem reads, is logged: it
    /// applies after a restart. Such a dimension that was released, and didn't change, is read again from its stem
    /// with the plugins loaded now, so that its level can generate again (see
    /// <see cref="WorldData.Generators.MojangGenerator.Resume"/>).
    /// </para>
    /// <para>
    /// The files' own dimensions, read as their plugins or packs loaded, are released (see
    /// <see cref="WorldgenDimension.Release"/>), so that they don't keep the plugins they're made of loaded; their stems
    /// are read again instead.
    /// </para>
    /// </remarks>
    /// <param name="files">The loaded plugins' files in load order, then data packs' files in theirs.</param>
    /// <returns>Whether a dimension was added or changed.</returns>
    public static bool Apply(IEnumerable<DimensionFile> files, ILogger logger)
    {
        lock (applying)
        {
            var all = files.ToList();
            var winners = VanillaOverrides.LastWins(all, file => ("", file.Identifier), file => file.Source, logger);
            var current = added;
            var changed = false;

            var read = new List<DimensionFile>();
            foreach (var file in winners)
            {
                if (Reread(file, logger) is DimensionFile fresh)
                    read.Add(fresh);
            }

            foreach (var file in all)
                ReleaseFile(file);

            // Vanilla's dimensions no stem overrides are read from vanilla's stems when what they name is overridden.
            foreach (var identifier in vanillaStems.Keys.Where(id => read.All(file => file.Identifier != id)))
            {
                try
                {
                    if (ReadVanilla(identifier, PluginNoise.Current) is DimensionFile vanilla)
                        read.Add(vanilla);
                }
                catch (InvalidDataException ex)
                {
                    Log.VanillaFailed(logger, ex, identifier);
                }
            }

            foreach (var file in read)
            {
                var existing = current.FirstOrDefault(entry => entry.Identifier == file.Identifier);

                if (!taken.ContainsKey(file.Identifier))
                {
                    if (existing is not null && !existing.Dimension.IsReleased && existing.Json == file.Json)
                    {
                        ReleaseFile(file);
                        continue;
                    }

                    if (existing is null)
                    {
                        current = current.Add(file);
                    }
                    else
                    {
                        current = current.Replace(existing, file);
                        ReleaseFile(existing);
                    }

                    changed = true;
                    continue;
                }

                // A dimension a level took stays as it was; a change to it, or an override of a vanilla one a level
                // took compiled, applies after a restart.
                if (existing is null || existing.Json != file.Json)
                {
                    if (!restartRequired.Contains(file.Identifier))
                        Log.ChangedUntilRestart(logger, file.Identifier, file.Source);

                    restartRequired = restartRequired.Add(file.Identifier);
                    ReleaseFile(file);
                    continue;
                }

                // The same stem read again with the plugins loaded now replaces a released dimension.
                if (existing.Dimension.IsReleased && !file.Dimension.IsReleased)
                {
                    current = current.Replace(existing, file);
                    changed = true;
                    continue;
                }

                ReleaseFile(file);
            }

            // Dimensions no file defines any more go, unless a level took them. Vanilla's are their compiled ones
            // again then, unless a level took them: that change applies after a restart.
            foreach (var file in current.Where(file => read.All(entry => entry.Identifier != file.Identifier)).ToList())
            {
                if (!taken.ContainsKey(file.Identifier))
                {
                    current = current.Remove(file);
                    ReleaseFile(file);
                    changed = true;
                    continue;
                }

                if (!IsVanilla(file.Identifier) || restartRequired.Contains(file.Identifier))
                    continue;

                Log.ChangedUntilRestart(logger, file.Identifier, file.Source);
                restartRequired = restartRequired.Add(file.Identifier);
            }

            // Released when a plugin they generate with unloaded, they're read again once it's back, unless they
            // changed. Those a file defines were read above.
            var released = current
                .Where(file => file.Dimension.IsReleased && !restartRequired.Contains(file.Identifier))
                .Where(file => winners.All(winner => winner.Identifier != file.Identifier))
                .ToList();

            foreach (var file in released)
            {
                if (Rebuild(file, logger) is DimensionFile rebuilt)
                {
                    current = current.Replace(file, rebuilt);
                    changed = true;
                }
            }

            // A level can't take those that left from a snapshot read before (see Take).
            foreach (var file in added.Except(current))
                file.Dimension.IsRemoved = true;

            added = current;

            return changed;
        }
    }

    /// <summary>
    /// The added dimension <paramref name="identifier"/>, released or not, or null. Of vanilla's, only those files
    /// override are added.
    /// </summary>
    public static LevelStem? Find(string identifier) =>
        added.FirstOrDefault(file => file.Identifier == identifier)?.Dimension;

    /// <summary>
    /// Whether the stem of the dimension <paramref name="identifier"/> a level took, or the plugin data it reads,
    /// changed since: the change applies after a restart, and a level of it that stopped stays stopped until then.
    /// </summary>
    public static bool RequiresRestart(string identifier) => restartRequired.Contains(identifier);

    /// <summary>
    /// The level stem the added dimension <paramref name="identifier"/> was read from: a plugin's or data pack's
    /// file, or vanilla's own stem for one of vanilla's dimensions whose noise is overridden. Null when there's none.
    /// </summary>
    /// <returns>Where the stem is from, for messages, and its JSON.</returns>
    public static (string Source, byte[] Stem)? FindStem(string identifier) =>
        added.FirstOrDefault(file => file.Identifier == identifier) is DimensionFile file
            ? (file.Source, file.Stem)
            : null;

    /// <summary>
    /// Vanilla's world preset's level stem of one of its dimensions, or null for another dimension. 26.3's are the
    /// same as 1.21.11's.
    /// </summary>
    public static byte[]? VanillaStem(string identifier) =>
        vanillaStems.TryGetValue(identifier, out var stem) ? Encoding.UTF8.GetBytes(stem.Stem) : null;

    /// <summary>
    /// The dimension types of vanilla's dimensions no level took yet whose build range, as a file overrides it, the
    /// dimension can't generate in, with why: Obsidian can't clamp generation to the build range, as vanilla does,
    /// so such a type has to stay vanilla's.
    /// </summary>
    public static Dictionary<string, string> VanillaTypeMisfits()
    {
        lock (applying)
        {
            var misfits = new Dictionary<string, string>();

            var untaken = WorldgenDimension.VanillaDimensions.Where(dimension => !taken.ContainsKey(dimension.Identifier));
            foreach (var compiled in untaken)
            {
                var dimension = Find(compiled.Identifier) as WorldgenDimension ?? compiled;
                if (dimension.IsReleased)
                    continue;

                var type = dimension.DimensionType;
                if (OutsideBuildRange(dimension) is string outside)
                    misfits.TryAdd(type.Name, outside);
            }

            return misfits;
        }
    }

    // Reads a file's stem again with the noise, types and biomes files define now, or gives null, logged, when it
    // can't be read any more.
    private static DimensionFile? Reread(DimensionFile file, ILogger logger)
    {
        try
        {
            return Read(file.Identifier, file.Source, file.Stem, PluginNoise.Current);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Reading creates plugins' types, whose code may fail.
            Log.RereadFailed(logger, ex, file.Identifier, file.Source);
            return null;
        }
    }

    private static void ReleaseFile(DimensionFile file)
    {
        if (!file.Dimension.IsReleased)
            file.Dimension.Release();
    }

    // Reads a released dimension's stem again, or returns null when it can't be: a plugin it generates with isn't
    // loaded, or the plugin data it reads changed, which applies after a restart.
    private static DimensionFile? Rebuild(DimensionFile file, ILogger logger)
    {
        DimensionFile rebuilt;

        try
        {
            rebuilt = Read(file.Identifier, file.Source, file.Stem, PluginNoise.Current);
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
    /// <see cref="WorldgenDimension.Release"/>), so that they don't keep its code loaded: those added, and those of
    /// the files plugins hold that weren't.
    /// </summary>
    /// <param name="held">The files plugins and data packs hold, the unloading plugin's included.</param>
    /// <returns>The added dimensions released.</returns>
    public static IReadOnlyList<LevelStem> Release(PluginContainer plugin, IEnumerable<DimensionFile> held)
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

            // Direct native pipelines also participate in release, including those outside registered levels.
            PluginWorldgen.Release(plugin);

            return released;
        }
    }

    // A vanilla world preset stem generating with vanilla's noise generator and a multi-noise preset.
    private static string NoiseStem(string type, string settings, string preset) => $$"""
        {
          "type": "minecraft:{{type}}",
          "generator": {
            "type": "minecraft:noise",
            "settings": "minecraft:{{settings}}",
            "biome_source": { "type": "minecraft:multi_noise", "preset": "minecraft:{{preset}}" }
          }
        }
        """;

    private static string? OutsideBuildRange(WorldgenDimension dimension) =>
        dimension.Settings.MinY < dimension.MinY
        || dimension.Settings.MinY + dimension.Settings.Height > dimension.MinY + dimension.Height
            ? "Noise settings exceed the dimension build range."
            : null;

    private static string VanillaType(string identifier) =>
        WorldgenDimension.VanillaDimensions.First(dimension => dimension.Identifier == identifier).DimensionType.Name;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Kept dimension {Identifier} as it was: the change in {Source} applies after a restart")]
        public static partial void ChangedUntilRestart(ILogger logger, string identifier, string source);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Left out dimension {Identifier} from {Source}: its level stem can't be read with the data "
                + "files define now")]
        public static partial void RereadFailed(ILogger logger, Exception exception, string identifier, string source);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Dimension {Identifier} stays stopped: reading its level stem again failed")]
        public static partial void RebuildFailed(ILogger logger, Exception exception, string identifier);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "Kept vanilla's {Identifier}: it can't generate with the noise settings or parameter list "
                + "files override")]
        public static partial void VanillaFailed(ILogger logger, Exception exception, string identifier);
    }
}

/// <summary>
/// A plugin's or data pack's level stem file, or vanilla's stem read at runtime.
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
    LevelStem Dimension);
