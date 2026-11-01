using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Features;
using Obsidian.WorldData.Generators.Terrain.Carvers;
using System.Threading;

namespace Obsidian.Registries;

internal static partial class PluginFeatures
{
    private static FeatureInputs inputs = new([], [], [], NullLogger.Instance);

    /// <summary>
    /// The live 26.3 registry. Replaced on the same successful apply as its shared feature adapter, including unload.
    /// </summary>
    public static FeatureRegistry CurrentRegistry => Volatile.Read(ref inputs).Registry.Value;

    /// <summary>The current 26.3 carvers, replaced with features on every successful apply.</summary>
    public static CarverRegistry CurrentCarvers => CurrentRegistry.Carvers;

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
        var resolver = new FeatureResolver(kept, WorldgenTypes.With(registrations), registry);
        registry.Carvers = CarverRegistry.Build(kept, biomes, registry.Data, logger, modifier =>
        {
            resolver.StepFeatures(modifier.Add.Features, "add.features");
            resolver.StepFeatures(modifier.Remove.Features, "remove.features");
        }, usePluginBlockTags: true);
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
