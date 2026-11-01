using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Features;
using System.Runtime.CompilerServices;
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

        // Values may reference their registry through feature adapters. Ephemeron keys allow a stale registry and
        // its layout to be collected together, even when an in-flight reader arrived after unload published us.
        private readonly ConditionalWeakTable<FeatureRegistry, WorldgenStructures> layouts = new();

        public StructureFile[] Files { get; } = files;

        public TemplateFile[] Templates { get; } = templates;

        public WorldgenTypeRegistration[] Types { get; } = types;

        public WorldgenStructures Read(FeatureRegistry features)
        {
            lock (this.reading)
            {
                if (this.layouts.TryGetValue(features, out var previous))
                    return previous;

                var structures = StructureData.Read(features, files, templates, types, logger);
                // The live snapshot's template lookup follows Apply immediately, even before a structure layout
                // is requested again. Explicit StructureData.Read callers may instead pin their own templates.
                features.TemplateOverrides = null;
                this.layouts.Add(features, structures);
                return structures;
            }
        }
    }
}
