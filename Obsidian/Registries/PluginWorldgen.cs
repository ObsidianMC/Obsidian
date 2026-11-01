using Obsidian.Plugins;
using Obsidian.WorldData.Generators.Biomes;
using Obsidian.WorldData.Generators.Density;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// The loaded plugins' and data packs' worldgen as the 26.3 pipeline reads it: their biomes, dimension types, noise
/// settings, density functions and noises laid over vanilla's 26.3 data (see <see cref="WorldgenData.With"/>), and
/// their dimensions read from their level stems (see <see cref="WorldgenDimension"/>).
/// </summary>
/// <remarks>
/// <para>
/// The files are those <see cref="PluginCodecs"/> and <see cref="PluginNoise"/> applied last, read as written: the
/// 26.3 pipeline reads 26.3's schemas, which changed from 1.21.11's for noise settings (<c>material_rule</c>, the
/// <c>aquifers</c> object), density functions (<c>left</c> and <c>right</c> operands, new types) and noises. A file
/// in 1.21.11's schema fails the dimensions naming it here, and one in 26.3's is left out of the 1.21.11 pipeline
/// with a warning, until the live generator switches.
/// </para>
/// <para>
/// A dimension keeps the data it was read with, like the 1.21.11 pipeline's; a plugin unloading releases those made of
/// its types or carvers (see <see cref="Release"/>), whose generators stop as it unloads.
/// </para>
/// </remarks>
internal static class PluginWorldgen
{
    // Gson reads data packs leniently, so comments and trailing commas are fine.
    private static readonly JsonDocumentOptions documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly Lock reading = new();

    // The data built from the files applied last, with the files it was built from: files hold data alone, unlike
    // the WorldgenNoise they're from, whose plugins' types would stay loaded.
    private static (IReadOnlyList<CodecFile> Codecs, IReadOnlyList<NoiseFile> Noise, WorldgenData Data)? current;

    // The dimensions read, which a plugin unloading releases; collected ones are dropped as they're found.
    private static ImmutableList<WeakReference<WorldgenDimension>> dimensions = [];

    /// <summary>
    /// Vanilla's 26.3 data with the entries of the files <see cref="PluginCodecs"/> and <see cref="PluginNoise"/>
    /// applied last.
    /// </summary>
    public static WorldgenData Data
    {
        get
        {
            var codecs = PluginCodecs.Applied;
            var noise = PluginNoise.Current.Files;

            lock (reading)
            {
                if (current is var (builtCodecs, builtNoise, built) && builtCodecs == codecs && builtNoise == noise)
                    return built;

                var data = Layer(WorldgenData.Vanilla, codecs, noise);
                current = (codecs, noise, data);

                return data;
            }
        }
    }

    /// <summary>
    /// Lays plugins' and data packs' files over 26.3 data: biomes and dimension types, and noise settings, density
    /// functions and noises. Other files aren't the 26.3 pipeline's to read here.
    /// </summary>
    /// <param name="codecs">Codec files in order; the last of an entry wins.</param>
    /// <param name="noise">Noise files in order; the last of an entry wins.</param>
    /// <exception cref="InvalidDataException">A codec file isn't JSON.</exception>
    public static WorldgenData Layer(
        WorldgenData below,
        IEnumerable<CodecFile> codecs,
        IEnumerable<NoiseFile> noise)
    {
        var entries = new List<WorldgenEntry>();

        foreach (var file in codecs)
        {
            if (file.Registry is not (PluginCodecs.Biome or PluginCodecs.DimensionType))
                continue;

            using var document = ParseCodec(file);
            var json = document.RootElement.Clone();

            entries.Add(new WorldgenEntry(Registry(file.Registry), file.Identifier, json));
        }

        foreach (var file in noise)
        {
            if (file.Registry == PluginNoise.ParameterList)
                continue;

            entries.Add(new WorldgenEntry(Registry(file.Registry), file.Identifier, file.Json));
        }

        return below.With(entries);
    }

    /// <summary>
    /// Reads a dimension for the 26.3 pipeline from its level stem as files define it now: the added one of its
    /// identifier (see <see cref="PluginDimensions.FindStem"/>), or else vanilla's own stem, with <see cref="Data"/>.
    /// Its biome regions are the loaded plugins' (see <see cref="PluginBiomeRegions"/>), fixed as its first generator
    /// is created.
    /// </summary>
    /// <exception cref="InvalidDataException">No stem defines it, or it isn't valid for the 26.3 pipeline.</exception>
    public static WorldgenDimension ReadDimension(string identifier)
    {
        var stem = PluginDimensions.FindStem(identifier)?.Stem
            ?? PluginDimensions.VanillaStem(identifier)
            ?? throw new InvalidDataException($"No level stem defines {identifier}.");

        return ReadDimension(identifier, stem, PluginNoise.Current, Data);
    }

    /// <summary>
    /// Reads a level stem for the 26.3 pipeline (see <see cref="WorldgenDimension.Read"/>), which a plugin unloading
    /// releases when it uses the plugin's types or carvers.
    /// </summary>
    /// <param name="noise">The worldgen types and parameter lists it can name.</param>
    /// <param name="data">The 26.3 data it's read from.</param>
    /// <exception cref="InvalidDataException">The stem isn't valid for the 26.3 pipeline.</exception>
    public static WorldgenDimension ReadDimension(
        string identifier,
        byte[] stem,
        WorldgenNoise noise,
        WorldgenData data)
    {
        var dimension = WorldgenDimension.Read(
            identifier,
            stem,
            noise,
            data,
            () => PluginBiomeRegions.Fix(identifier),
            () => PluginFeatures.CurrentCarvers);

        if (!dimension.Plugins.IsEmpty)
        {
            var reference = new WeakReference<WorldgenDimension>(dimension);
            ImmutableInterlocked.Update(ref dimensions, list => list.Add(reference));
        }

        return dimension;
    }

    /// <summary>
    /// Releases the dimensions read that use an unloading plugin's types or carvers (see
    /// <see cref="WorldgenDimension.Release"/>), so that they don't keep its code loaded.
    /// </summary>
    public static void Release(PluginContainer plugin)
    {
        // The data holds no plugin's code, but is built again from the files applied once the plugin is gone.
        lock (reading)
            current = null;

        foreach (var reference in Volatile.Read(ref dimensions))
        {
            if (reference.TryGetTarget(out var dimension) && dimension.Plugins.Contains(plugin))
                dimension.Release();
        }

        ImmutableInterlocked.Update(ref dimensions, list => list.RemoveAll(IsGone));

        static bool IsGone(WeakReference<WorldgenDimension> reference) =>
            !reference.TryGetTarget(out var dimension) || dimension.IsReleased;
    }

    // A data pack folder as the 26.3 data names its registry: worldgen/biome is biome.
    private static string Registry(string folder)
    {
        const string prefix = "worldgen/";

        return folder.StartsWith(prefix, StringComparison.Ordinal) ? folder[prefix.Length..] : folder;
    }

    private static JsonDocument ParseCodec(CodecFile file)
    {
        try
        {
            return JsonDocument.Parse(file.Json, documentOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{file.Source} isn't JSON: {ex.Message}", ex);
        }
    }
}
