using Obsidian.SourceGenerators.Packets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.SourceGenerators.Registry;
public partial class RegistryAssetsGenerator
{
    private static void GenerateEnum(string enumsJson, SourceProductionContext ctx)
    {
        var enums = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(enumsJson)!;

        var builder = new CodeBuilder();

        foreach (var elements in enums.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var isAttribute = elements.Key == "minecraft:attribute";
            var enumName = isAttribute ? "AttributeIds" : elements.Key.RemoveNamespace().ToPascalCase();
            var components = elements.Value.Deserialize<Dictionary<string, Protocol>>();

            builder.Namespace(isAttribute ? "Obsidian.API.Inventory" : "Obsidian.API")
             .Line()
             .Type(isAttribute ? $"public static partial class {enumName}" : $"public enum {enumName}");

            foreach (var component in components.OrderBy(x => x.Value.ProtocolId).ThenBy(x => x.Key, StringComparer.Ordinal))
            {
                // Keep the API's existing British spelling of vanilla's campfire_cozy_smoke.
                var name = (elements.Key, component.Key) switch
                {
                    ("minecraft:particle_type", "minecraft:campfire_cozy_smoke") => "CampfireCosySmoke",
                    _ => component.Key.RemoveNamespace().ToPascalCase()
                };
                var protocolId = component.Value.ProtocolId;

                builder.Line(isAttribute ? $"public const int {name} = {protocolId};" : $"{name} = {protocolId},");
            }

            builder.EndScope();

            ctx.AddSource($"{enumName}.g.cs", builder.ToString());

            builder.Clear();
        }
    }

    private sealed class Protocol
    {
        [JsonPropertyName(Vocabulary.ProtocolId)]
        public int ProtocolId { get; set; }
    }
}
