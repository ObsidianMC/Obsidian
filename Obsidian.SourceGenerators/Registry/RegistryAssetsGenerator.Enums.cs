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

        foreach (var elements in enums)
        {
            var enumName = elements.Key.RemoveNamespace().ToPascalCase();
            var components = elements.Value.Deserialize<Dictionary<string, Protocol>>();

            builder.Namespace("Obsidian.API")
             .Line()
             .Type($"public enum {enumName}");

            foreach (var component in components.OrderBy(x => x.Value.ProtocolId))
            {
                var name = component.Key.RemoveNamespace().ToPascalCase();
                var protocolId = component.Value.ProtocolId;

                builder.Line($"{name} = {protocolId},");
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
