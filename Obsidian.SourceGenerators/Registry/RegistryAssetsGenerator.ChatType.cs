using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;
public partial class RegistryAssetsGenerator
{
    private static void GenerateChatType(Codec[] chatTypes, CodeBuilder builder)
    {
        builder.Type($"public static partial class ChatType");

        builder.Indent()
            .Append("public const string CodecKey = \"minecraft:chat_type\";").Line().Line();
        builder.Indent()
            .Append($"public const int GlobalBitsPerEntry = {(int)Math.Ceiling(Math.Log(chatTypes.Length, 2))};").Line().Line();

        foreach (var codec in chatTypes)
        {
            var propertyName = codec.Name.RemoveNamespace().ToPascalCase();

            builder.Indent()
                .Append($"public static ChatCodec {propertyName} => All[\"{codec.Name}\"];")
                .Line();
        }

        builder.Line()
            .Indent()
            .Append("internal static ConcurrentDictionary<string, ChatCodec> All { get; } = new();")
            .Line();

        builder.EndScope();
    }


}
