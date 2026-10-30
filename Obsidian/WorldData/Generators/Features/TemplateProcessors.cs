using System.Text.Json;
using System.Text.Json.Nodes;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private static JsonElement TemplateProcessorStates(JsonElement json)
    {
        var processors = JsonNode.Parse(json.GetRawText())!;
        NormalizeProcessorStates(processors);
        return JsonSerializer.SerializeToElement(processors);
    }

    private static void NormalizeProcessorStates(JsonNode node)
    {
        if (node is JsonArray array)
        {
            foreach (var entry in array)
            {
                if (entry is not null)
                {
                    NormalizeProcessorStates(entry);
                }
            }

            return;
        }

        if (node is not JsonObject fields)
        {
            return;
        }

        var blockIgnore = fields["processor_type"]?.GetValue<string>() == "minecraft:block_ignore";

        foreach (var field in fields.ToArray())
        {
            if (field.Value is null)
            {
                continue;
            }

            if (field.Key == "output_state")
            {
                fields[field.Key] = ProcessorBlockState(field.Value);
            }
            else if (blockIgnore && field.Key == "blocks" && field.Value is JsonArray blocks)
            {
                fields[field.Key] = new JsonArray(blocks.Select(block => ProcessorBlockState(block!)).ToArray());
            }
            else
            {
                NormalizeProcessorStates(field.Value);
            }
        }
    }

    private static JsonObject ProcessorBlockState(JsonNode node)
    {
        // The shared processor algorithms still read SimpleBlockState's legacy Name/Properties representation.
        // Translate 26.3's compact strings and id/properties objects here, without changing the legacy reader.
        if (node is JsonValue value)
        {
            return new JsonObject { ["Name"] = value.GetValue<string>() };
        }

        var name = node["id"] ?? node["Name"];
        var properties = node["properties"] ?? node["Properties"];

        return new JsonObject
        {
            ["Name"] = name?.DeepClone(),
            ["Properties"] = properties?.DeepClone() ?? new JsonObject()
        };
    }
}
