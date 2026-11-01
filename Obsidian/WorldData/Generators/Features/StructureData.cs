using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.Plugins;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Structures.Pools;
using System.Collections.Frozen;

namespace Obsidian.WorldData.Generators.Features;

/// <summary>
/// Reads 26.3 structures with the shared plugin resolver: the same registration, overrides, ownership, cycles and
/// invalid-file isolation as the live generator, using 26.3's features, processors and vanilla entries.
/// </summary>
internal static class StructureData
{
    public static WorldgenStructures Read(FeatureRegistry features) => features.UsesPluginFiles
        ? PluginStructures.ReadRegistry(features)
        : Read(features, [], [], [], NullLogger.Instance);

    public static WorldgenStructures Read(
        FeatureRegistry features,
        IEnumerable<StructureFile> files,
        IEnumerable<TemplateFile> templates,
        IEnumerable<WorldgenTypeRegistration> types,
        ILogger logger)
    {
        var vanillaFiles = new[] { "structure", "structure_set", "template_pool" }
            .SelectMany(registry => features.Data.Ids(registry).Select(id => new StructureFile(
                $"worldgen/{registry}",
                id,
                $"vanilla's {id}",
                features.Data.Read(registry, id))))
            .ToArray();
        var empty = new WorldgenStructures
        {
            Version = 0,
            Structures = FrozenDictionary<string, Structure>.Empty,
            Sets = FrozenDictionary<string, StructureSet>.Empty,
            Pools = FrozenDictionary<string, StructureTemplatePool>.Empty,
            Templates = FrozenDictionary<string, StructureTemplate>.Empty,
            Owners = FrozenDictionary<Structure, ImmutableArray<PluginContainer>>.Empty,
            LegacyPools = false
        };
        var featureSet = features.AsFeatureSet();
        var registrations = types.ToArray();
        var pluginFiles = files.ToArray();
        var vanilla = PluginStructures.Build(
            Normalize(vanillaFiles), [], featureSet, registrations, logger, empty);
        var structures = pluginFiles.Length == 0
            ? vanilla with { Templates = LastTemplates(templates, logger) }
            : PluginStructures.Build(
                Normalize(vanillaFiles.Concat(pluginFiles)),
                templates,
                featureSet,
                registrations,
                logger,
                vanilla);

        foreach (var pool in structures.Pools.Values)
        {
            foreach (var entry in pool.Elements)
                UnnameFeatureJigsaws(entry.Element);
        }

        features.TemplateOverrides = structures.Templates;
        return structures;
    }

    private static IEnumerable<StructureFile> Normalize(IEnumerable<StructureFile> files) =>
        files.Select(file => file with { Json = FeatureRegistry.TemplateProcessorStates(file.Json) });

    private static FrozenDictionary<string, StructureTemplate> LastTemplates(
        IEnumerable<TemplateFile> templates,
        ILogger logger) => VanillaOverrides.LastWins(
            templates,
            file => ("structure", file.Identifier),
            file => file.Source,
            logger).ToFrozenDictionary(file => file.Identifier, file => file.Template);

    // 26.3's feature elements have a jigsaw without a name.
    private static void UnnameFeatureJigsaws(StructurePoolElement element)
    {
        if (element is FeaturePoolElement feature)
        {
            feature.UnnamedJigsaw = true;
        }
        else if (element is ListPoolElement list)
        {
            foreach (var child in list.Elements)
                UnnameFeatureJigsaws(child);
        }
    }
}
