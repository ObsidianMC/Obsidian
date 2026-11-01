using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Features;
using System.Threading;

namespace Obsidian.Registries;

internal static partial class PluginStructures
{
    private static StructureInputs inputs = new([], [], [], NullLogger.Instance);

    /// <summary>The 26.3 layout, refreshed with plugin files, types, templates and feature dependencies.</summary>
    public static WorldgenStructures CurrentRegistry => ReadRegistry(PluginFeatures.CurrentRegistry);

    internal static WorldgenStructures ReadRegistry(FeatureRegistry features) =>
        Volatile.Read(ref inputs).Read(features);

    private sealed class StructureInputs(
        StructureFile[] files,
        TemplateFile[] templates,
        WorldgenTypeRegistration[] types,
        ILogger logger)
    {
        private readonly Lock reading = new();
        private FeatureRegistry? previousFeatures;
        private WorldgenStructures? previous;

        public StructureFile[] Files { get; } = files;

        public TemplateFile[] Templates { get; } = templates;

        public WorldgenTypeRegistration[] Types { get; } = types;

        public WorldgenStructures Read(FeatureRegistry features)
        {
            lock (this.reading)
            {
                if (ReferenceEquals(features, this.previousFeatures))
                    return this.previous!;

                var structures = StructureData.Read(features, files, templates, types, logger);
                // The live snapshot's template lookup follows Apply immediately, even before a structure layout
                // is requested again. Explicit StructureData.Read callers may instead pin their own templates.
                features.TemplateOverrides = null;
                this.previousFeatures = features;
                this.previous = structures;
                return structures;
            }
        }
    }
}
