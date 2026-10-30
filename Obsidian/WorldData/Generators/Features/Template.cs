using Obsidian.WorldData.Features;
using Obsidian.WorldData.Structures;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    /// <summary>
    /// The generator's 26.3 template lookup. Kept explicit while the live structure registry still loads 1.21.11.
    /// Callers can supply data pack templates without putting copyrighted NBT in Obsidian's assets.
    /// </summary>
    public Func<string, StructureTemplate> Template { get; init; } = _ =>
        throw new InvalidOperationException("The 26.3 feature registry needs a 26.3 structure template lookup.");

    private Feature TemplateFeature(JsonElement json)
    {
        var entries = ValueProviders.Weighted(json.GetProperty("templates"), ReadTemplateEntry);
        StructureProcessorList? processors = null;

        if (json.TryGetProperty("processors", out var processorJson))
        {
            if (processorJson.ValueKind == JsonValueKind.String)
            {
                processorJson = this.Data.Read("processor_list", processorJson.GetString()!);
            }

            var reader = new WorldgenReader(
                WorldgenTypes.BuiltIn,
                _ => throw new InvalidOperationException("A template processor cannot resolve a configured feature."),
                _ => throw new InvalidOperationException("A template processor cannot resolve a placed feature."));
            processors = reader.ReadProcessorList(TemplateProcessorStates(processorJson), "");
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
