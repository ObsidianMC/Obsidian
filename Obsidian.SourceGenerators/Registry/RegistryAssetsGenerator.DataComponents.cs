using Obsidian.SourceGenerators.Packets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.SourceGenerators.Registry;
public partial class RegistryAssetsGenerator
{
    private static void GenerateDataComponentsEnum(string dataComponentsJson, SourceProductionContext ctx)
    {
        var components = JsonSerializer.Deserialize<Dictionary<string, Protocol>>(dataComponentsJson)!;

        var builder = new CodeBuilder()
           .Namespace("Obsidian.API")
           .Line()
           .Type("public enum DataComponentType");

        foreach (var component in components.OrderBy(x => x.Value.ProtocolId))
        {
            var name = component.Key.RemoveNamespace().ToPascalCase();
            var protocolId = component.Value.ProtocolId;

            builder.Line($"{name} = {protocolId},");
        }

        builder.EndScope();

        ctx.AddSource("DataComponentType.g.cs", builder.ToString());
    }

    private sealed class Protocol
    {
        [JsonPropertyName(Vocabulary.ProtocolId)]
        public int ProtocolId { get; set; }
    }
}
