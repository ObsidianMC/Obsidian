using Microsoft.Extensions.Logging;
using Obsidian.Plugins;
using Obsidian.Registries.Codecs;
using Obsidian.WorldData.Generators.Mojang;
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
/// A dimension added stays until the server stops, even once its plugin unloads, with the stem and noise settings it
/// was read with: changing a live level's generator would mix two generators' chunks. For the same reason a reload
/// changing a stem, or the plugin data it reads, keeps the dimension as it was, with a warning, until a restart.
/// A dimension whose generator is made of a plugin's types can't outlive the plugin's code, though: when the plugin
/// unloads, it's released (see <see cref="Release"/>) and its level stops generating new chunks. Once the plugin is
/// back, the dimension is read again from the stem it was added with (see <see cref="Apply"/>), and its level
/// generates again, unless its stem or the plugin data it reads changed: then it stays stopped until a restart.
/// When files define the same dimension, the last in order wins.
/// </para>
/// <para>
/// Vanilla's dimensions (<see cref="MojangDimension.Vanilla"/>) are built from compiled code, unless a file overrides
/// their stem, or the noise settings or parameter list their stem names: then they're read at runtime, from the file
/// or from vanilla's own stem. Like vanilla, which reads a data pack's stems over those saved in level.dat, the
/// overrides apply to every world when it loads, and to the chunks generated from then on. Until a level takes a
/// vanilla dimension (see <see cref="TakeVanilla"/>), it follows the files; once one has, it's kept like a plugin's.
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

    // Vanilla 1.21.11's world preset's level stems, by dimension, with the parameter list each names: what they name
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

    // Vanilla's dimensions a level generates with, which are kept from then on.
    private static ImmutableHashSet<string> taken = [];

    /// <summary>
    /// The plugins' and data packs' dimensions added since the server started, in the order they were added; vanilla's
    /// overridden ones aren't.
    /// </summary>
    public static ImmutableArray<MojangDimension> Added =>
        [.. added.Where(file => !IsVanilla(file.Identifier)).Select(file => file.Dimension)];

    /// <summary>
    /// Every dimension generated with vanilla's generator: vanilla's, as overridden, and plugins'.
    /// </summary>
    public static IEnumerable<MojangDimension> All =>
        MojangDimension.Vanilla.Select(dimension => Find(dimension.Identifier) ?? dimension).Concat(Added);

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
        WorldgenNoise? noise = null)
    {
        if (!PluginTags.IsFileIdentifier(identifier))
            throw new InvalidDataException($"{source}: {identifier} isn't a valid identifier.");

        noise ??= PluginNoise.Current;

        try
        {
            var bytes = json.ToArray();
            using var document = JsonDocument.Parse(bytes, documentOptions);
            var entries = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var dimension = LevelStemData.Read(identifier, document.RootElement, noise, entries);

            // Obsidian creates vanilla's levels with vanilla's dimension types; a stem can change those types' data.
            if (vanillaStems.ContainsKey(identifier) && dimension.DimensionType.Name != VanillaType(identifier))
            {
                dimension.Release();

                throw CodecJson.Error(
                    "type",
                    $"{identifier} must keep its dimension type {VanillaType(identifier)}: override that type instead");
            }

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
    /// Reads one of vanilla's dimensions from vanilla's stem at runtime when a file overrides the noise settings or
    /// parameter list it names; otherwise its compiled dimension stays, and this is null.
    /// </summary>
    /// <param name="identifier">One of vanilla's dimensions, like <c>minecraft:overworld</c>.</param>
    /// <param name="noise">The noise entries files define.</param>
    /// <exception cref="InvalidDataException">The overrides it names can't generate it.</exception>
    public static DimensionFile? ReadVanilla(string identifier, WorldgenNoise noise)
    {
        var (stem, parameterList) = vanillaStems[identifier];
        var compiled = MojangDimension.Vanilla.First(dimension => dimension.Identifier == identifier);

        var overridden = noise.NoiseSettings.ContainsKey(compiled.NoiseSettings!)
            || parameterList is not null && noise.ParameterLists.ContainsKey(parameterList);

        return overridden
            ? Parse(identifier, $"vanilla's {identifier}", Encoding.UTF8.GetBytes(stem), noise)
            : null;
    }

    /// <summary>
    /// The dimension a level of one of vanilla's dimensions generates with: as files override it now, or else the
    /// compiled one. It's kept from then on: later changes apply after a restart.
    /// </summary>
    /// <remarks>
    /// This is where vanilla's dimensions are built for their levels, so what changes how they generate as a whole,
    /// like a wrapper around their biome source, goes here or in <see cref="LevelStemData"/>.
    /// </remarks>
    public static MojangDimension TakeVanilla(MojangDimension compiled)
    {
        lock (applying)
        {
            taken = taken.Add(compiled.Identifier);
            return Find(compiled.Identifier) ?? compiled;
        }
    }

    /// <summary>
    /// Adds the files' dimensions that weren't added yet. Ones added before stay as they were, and those whose file
    /// changed are logged: the change applies after a restart. Added dimensions that were released, and didn't change,
    /// are read again from their stems with the plugins loaded now, so that their levels can generate again (see
    /// <see cref="WorldData.Generators.MojangGenerator.Resume"/>). Vanilla's dimensions no level took yet follow the
    /// files, overrides of the noise they name included (see <see cref="ReadVanilla"/>).
    /// </summary>
    /// <remarks>
    /// A file whose dimension isn't kept, because a later file defines it or the dimension was added before,
    /// is released (see <see cref="MojangDimension.Release"/>), so that it doesn't keep the plugins it's made of
    /// loaded. A released file that wasn't added isn't added: its plugin is reloaded to read it again.
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

            var kept = new HashSet<DimensionFile>(winners, ReferenceEqualityComparer.Instance);
            foreach (var file in all.Where(file => !kept.Contains(file)))
                Discard(file);

            // Vanilla's dimensions no stem overrides are read from vanilla's stems when what they name is overridden.
            foreach (var identifier in vanillaStems.Keys.Where(id => winners.All(file => file.Identifier != id)))
            {
                try
                {
                    if (ReadVanilla(identifier, PluginNoise.Current) is DimensionFile vanilla)
                        winners.Add(vanilla);
                }
                catch (InvalidDataException ex)
                {
                    Log.VanillaFailed(logger, ex, identifier);
                }
            }

            foreach (var file in winners)
            {
                var existing = current.FirstOrDefault(entry => entry.Identifier == file.Identifier);
                if (ReferenceEquals(existing, file))
                    continue;

                // A vanilla dimension no level took yet follows the files.
                if (IsVanilla(file.Identifier) && !taken.Contains(file.Identifier))
                {
                    if (existing is not null)
                    {
                        current = current.Remove(existing);
                        Discard(existing);
                    }

                    if (!file.Dimension.IsReleased)
                        current = current.Add(file);

                    changed = true;
                    continue;
                }

                // A vanilla dimension a level took compiled, now overridden, changed too.
                if (existing is not null || IsVanilla(file.Identifier))
                {
                    // The same stem read again with the plugins loaded now replaces a released dimension.
                    if (existing is null || existing.Json != file.Json)
                    {
                        if (!restartRequired.Contains(file.Identifier))
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

            // Vanilla's dimensions nothing overrides any more are vanilla's compiled ones again, unless a level took
            // them: that change applies after a restart.
            foreach (var file in current.Where(file => IsVanilla(file.Identifier)).ToList())
            {
                if (winners.Any(winner => winner.Identifier == file.Identifier))
                    continue;

                if (taken.Contains(file.Identifier))
                {
                    if (restartRequired.Contains(file.Identifier))
                        continue;

                    Log.ChangedUntilRestart(logger, file.Identifier, file.Source);
                    restartRequired = restartRequired.Add(file.Identifier);
                    continue;
                }

                current = current.Remove(file);
                Discard(file);
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
    /// The added dimension <paramref name="identifier"/>, released or not, or null. Of vanilla's, only those files
    /// override are added.
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
    /// <param name="held">The files plugins and data packs hold, the unloading plugin's included.</param>
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

    private static string VanillaType(string identifier) =>
        MojangDimension.Vanilla.First(dimension => dimension.Identifier == identifier).DimensionType.Name;

    private static partial class Log
    {
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
    MojangDimension Dimension);
