using Obsidian.API.World.Generator;
using Obsidian.Plugins;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Biomes;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Mojang;
using System.IO;
using System.Text.Json;

namespace Obsidian.Registries.Codecs;

// A level stem's biome source as the 26.3 pipeline generates it (see WorldgenDimension).
internal static partial class LevelStemData
{
    /// <summary>
    /// Reads a noise generator's <c>biome_source</c> into the 26.3 pipeline's layout: the same kinds as
    /// <see cref="Read"/> takes, with 26.3's parameter lists.
    /// </summary>
    /// <param name="noise">The worldgen types and parameter lists the source can name.</param>
    /// <param name="data">The 26.3 data a preset's parameter list is read from.</param>
    /// <param name="plugins">Gets the plugins whose types a plugin's biome source is made of.</param>
    /// <exception cref="InvalidDataException">The source isn't valid.</exception>
    public static BiomeLayout ReadLayout(
        JsonElement value,
        string path,
        WorldgenNoise noise,
        WorldgenData data,
        HashSet<PluginContainer> plugins,
        bool legacyRandomSource)
    {
        CodecJson.Object(value, path);
        var typePath = CodecJson.Field(path, "type");
        var type = CodecJson.Identifier(CodecJson.Required(value, path, "type"), typePath);

        switch (type)
        {
            case "minecraft:fixed":
            {
                var biome = Biome(CodecJson.Required(value, path, "biome"), CodecJson.Field(path, "biome"));
                return new PositionalLayout(new FixedBiomeSource(biome));
            }
            case "minecraft:checkerboard":
            {
                var biomes = BiomeList(CodecJson.Required(value, path, "biomes"), CodecJson.Field(path, "biomes"));
                var scale = CodecJson.TryGet(value, "scale", out var scaleValue) ? scaleValue : (JsonElement?)null;
                return new PositionalLayout(new CheckerboardBiomeSource(biomes, Scale(scale)));
            }
            case "minecraft:multi_noise":
                return MultiNoiseLayout(value, path, noise, data);
            case "minecraft:the_end":
                return EndLayout.Instance;
            default:
                if (noise.Types.Find(type, typeof(BiomeSourceBase)) is null)
                {
                    throw CodecJson.Error(
                        typePath,
                        $"{type} isn't a biome source type: expected minecraft:multi_noise, minecraft:fixed, "
                        + "minecraft:checkerboard, minecraft:the_end or a type a plugin registered");
                }

                return NativePluginLayout(value, path, noise, data, plugins, legacyRandomSource);
        }
    }

    // Source properties use the same native density/noise adapters as terrain plugins. Discover owners with a
    // placeholder seed, then retain only the source's types and rebuild its seeded values for each world.
    private static PluginLayout NativePluginLayout(JsonElement value, string path, WorldgenNoise noise,
        WorldgenData data, HashSet<PluginContainer> plugins, bool legacyRandomSource)
    {
        var used = new HashSet<Type>();
        var density = new PluginDensityTypes(noise.Types);
        var compiler = new DensityCompiler(data, 0, legacyRandomSource, types: density);
        var reader = SourceReader(noise.Types, compiler, type =>
        {
            used.Add(type);
            if (WorldgenDependencies<PluginContainer>.Owner(type) is PluginContainer owner)
                plugins.Add(owner);
        });
        var (source, biomes) = ReadPluginSource(value, path, noise, plugins, null, reader);
        plugins.UnionWith(density.Owners);

        var factory = SourceFactory(value.Clone(), path, data, noise.Types.Narrow(used),
            density.Narrowed(), legacyRandomSource);
        return new PluginLayout(source, biomes, factory);
    }

    private static Func<long, BiomeSourceBase> SourceFactory(JsonElement value, string path, WorldgenData data,
        WorldgenTypes types, PluginDensityTypes? density, bool legacyRandomSource) => seed =>
    {
        var compiler = new DensityCompiler(data, seed, legacyRandomSource, types: density);
        return SourceReader(types, compiler).Read<BiomeSourceBase>(value, path);
    };

    private static WorldgenReader SourceReader(WorldgenTypes types, DensityCompiler compiler,
        Action<Type>? created = null) => new(types,
            id => throw new InvalidDataException($"{id}: features can't be used in a biome source"),
            id => throw new InvalidDataException($"{id}: features can't be used in a biome source"), created)
        {
            DensityFunctions = (json, path) => new CompiledDensityFunction(compiler.Compile(json, path)),
            Noises = (json, target, path) => PluginDensityTypes.Noise(json, target, path, compiler)
        };

    // Like vanilla's Codec.mapEither: an inline biomes list, or else a parameter list naming one of 26.3's presets.
    private static MultiNoiseLayout MultiNoiseLayout(
        JsonElement value,
        string path,
        WorldgenNoise noise,
        WorldgenData data)
    {
        InvalidDataException? listError = null;

        if (CodecJson.TryGet(value, "biomes", out var list))
        {
            try
            {
                var entries = ParameterList(list, CodecJson.Field(path, "biomes"))
                    .Select(entry => (entry.Point, entry.Biome.Name))
                    .ToList();

                return new MultiNoiseLayout(BiomeParameters.FromEntries(entries));
            }
            catch (InvalidDataException ex)
            {
                listError = ex;
            }
        }

        if (!CodecJson.TryGet(value, "preset", out var preset))
            throw listError ?? CodecJson.Error(path, "expected a biomes list or a preset");

        var presetPath = CodecJson.Field(path, "preset");
        ParameterListPreset parameters;

        if (preset.ValueKind == JsonValueKind.Object)
        {
            parameters = ParameterListPreset.Read(preset, presetPath);
        }
        else
        {
            var identifier = CodecJson.Identifier(preset, presetPath);
            if (!noise.TryGetParameterList(identifier, out var named))
                throw CodecJson.Error(presetPath, $"{identifier} isn't a multi-noise parameter list");

            parameters = named;
        }

        return new MultiNoiseLayout(BiomeParameters.Read(data, parameters.Preset));
    }
}
