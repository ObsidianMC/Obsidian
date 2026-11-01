using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Features;
using System.Threading;

namespace Obsidian.Registries;

internal static partial class PluginFeatures
{
    private static FeatureInputs inputs = new([], [], [], NullLogger.Instance);

    /// <summary>
    /// The 26.3 counterpart of <see cref="Current"/>. Replaced on the same successful apply, including unload.
    /// The live generator still uses Current; switching it must also refresh its decoration and structure layout.
    /// </summary>
    public static FeatureRegistry CurrentRegistry => Volatile.Read(ref inputs).Registry.Value;

    /// <summary>
    /// Builds a 26.3 registry using the shared plugin biome modifiers, ownership and file failure handling.
    /// Its FeatureSet can serve plant growth through the existing WorldgenFeatureSet interface.
    /// </summary>
    public static FeatureRegistry BuildRegistry(
        IEnumerable<FeatureFile> files,
        IEnumerable<CodecFile> biomes,
        IEnumerable<WorldgenTypeRegistration> types,
        ILogger logger,
        WorldgenData? data = null)
    {
        var kept = VanillaOverrides.LastWins(
            files,
            file => (file.Registry == ConfiguredFeature ? Feature : file.Registry, file.Identifier),
            file => file.Source,
            logger);
        var registrations = types.ToArray();
        var registry = new FeatureRegistry(data ?? WorldgenData.Vanilla);
        registry.ConfigurePlugins(kept, registrations);
        registry.FeatureSet = Build(kept, biomes, registrations, logger, registry: registry);
        return registry;
    }

    private sealed class FeatureInputs(
        FeatureFile[] files,
        CodecFile[] biomes,
        WorldgenTypeRegistration[] types,
        ILogger logger)
    {
        public FeatureFile[] Files { get; } = files;

        public CodecFile[] Biomes { get; } = biomes;

        public WorldgenTypeRegistration[] Types { get; } = types;

        public Lazy<FeatureRegistry> Registry { get; } = new(() => BuildRegistry(files, biomes, types, logger));
    }
}
