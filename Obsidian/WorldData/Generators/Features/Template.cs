using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Structures;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private Func<string, StructureTemplate> vanillaTemplate = Registries.StructureRegistry.GetCurrent;

    internal IReadOnlyDictionary<string, StructureTemplate>? TemplateOverrides { get; set; }

    /// <summary>
    /// The generator's 26.3 template lookup: the templates the server extracts from the 26.3 jar by default (see
    /// <see cref="Registries.StructureRegistry.GetCurrent"/>). Callers can supply others, such as a data pack's.
    /// </summary>
    public Func<string, StructureTemplate> Template
    {
        get => this.FindTemplate;
        init => this.vanillaTemplate = value;
    }

    private StructureTemplate FindTemplate(string id)
    {
        id = WorldgenData.Qualify(id);
        var templates = this.TemplateOverrides;
        if (templates is null && this.UsesPluginFiles)
            templates = PluginStructures.CurrentRegistry.Templates;

        return templates?.GetValueOrDefault(id) ?? this.vanillaTemplate(id);
    }

    private Feature TemplateFeature(JsonElement json)
    {
        var entries = ValueProviders.Weighted(json.GetProperty("templates"), ReadTemplateEntry);
        StructureProcessorList? processors = null;

        if (json.TryGetProperty("processors", out var processorJson))
        {
            if (processorJson.ValueKind == JsonValueKind.String)
            {
                processors = this.Processors(processorJson.GetString()!);
            }
            else
            {
                processors = this.ReadProcessors(processorJson);
            }
        }

        return (context, random, origin) =>
        {
            var entry = entries.Pick(random)
                ?? throw new InvalidOperationException("A template feature needs a nonempty weighted template list.");
            var rotation = entry.Rotations[random.NextInt(entry.Rotations.Length)];
            var template = this.Template(entry.Id);
            var localOffset = new Vector(-template.Size.X / 2, 0, -template.Size.Z / 2);
            var offset = StructureTemplate.Transform(localOffset, rotation, Vector.Zero);
            var corner = origin + offset;
            var settings = new StructurePlaceSettings
            {
                Rotation = rotation,
                Random = random
            };

            if (processors is not null)
            {
                settings.Processors.AddRange(processors.Processors);
            }

            return template.PlaceInWorld(context.Level, corner, corner, settings, random);
        };
    }

    private static TemplateEntry ReadTemplateEntry(JsonElement json)
    {
        var id = json.GetProperty("id").GetString()!;
        var rotations = json.TryGetProperty("rotations", out var values)
            ? values.EnumerateArray().Select(value => ReadRotation(value.GetString()!)).ToArray()
            : Enum.GetValues<StructureRotation>();

        return new TemplateEntry(id, rotations);
    }

    private static StructureRotation ReadRotation(string rotation) => rotation switch
    {
        "none" => StructureRotation.None,
        "clockwise_90" => StructureRotation.Clockwise90,
        "180" => StructureRotation.Clockwise180,
        "counterclockwise_90" => StructureRotation.CounterClockwise90,
        _ => throw new ArgumentException($"Unknown structure rotation {rotation}.")
    };

    private sealed record TemplateEntry(string Id, StructureRotation[] Rotations);
}
