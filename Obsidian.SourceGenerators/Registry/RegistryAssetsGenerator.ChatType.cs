using Obsidian.SourceGenerators.Registry.Models;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;
public partial class RegistryAssetsGenerator
{
    private static void GenerateChatType(Codec[] chatTypes, CodeBuilder builder, SourceProductionContext ctx)
    {
        builder.Type($"public static class ChatType");

        builder.Indent().Append("public const string CodecKey = \"minecraft:chat_type\";").Line().Line();

        foreach (var chatType in chatTypes)
        {
            var propertyName = chatType.Name.RemoveNamespace().ToPascalCase();

            builder.Indent().Append($"public static ChatCodec {propertyName} {{ get; }} = new() {{ Id = {chatType.RegistryId}, Name = \"{chatType.Name}\", Element = new() {{ ");

            foreach (var property in chatType.Properties)
            {
                var name = property.Key;
                var value = property.Value;

                builder.Append($"{name} = ");

                if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ParseProperty(builder, value, ctx);
                    continue;
                }

                AppendValueType(builder, value, ctx);
            }

            builder.Append("} };").Line();
        }

        builder.Line().Statement("public static IReadOnlyDictionary<string, ChatCodec> All { get; } = new Dictionary<string, ChatCodec>");

        foreach (var name in chatTypes.Select(x => x.Name))
        {
            var propertyName = name.RemoveNamespace().ToPascalCase();
            builder.Line($"{{ \"{name}\", {propertyName} }},");
        }

        builder.EndScope(".AsReadOnly()", true).Line();

        builder.EndScope();
    }


}
