using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;
public partial class RegistryAssetsGenerator
{
    private static void GenerateDamageTypes(Codec[] damageTypes, CodeBuilder builder)
    {
        builder.Type($"public static partial class DamageTypes");

        builder.Indent()
            .Append("public const string CodecKey = \"minecraft:damage_type\";").Line().Line();

        builder.Indent()
            .Append($"public const int GlobalBitsPerEntry = {(int)Math.Ceiling(Math.Log(damageTypes.Length, 2))};").Line().Line();

        foreach (var codec in damageTypes)
        {
            var propertyName = codec.Name.RemoveNamespace().ToPascalCase();

            builder.Indent()
                .Append($"public static DamageTypeCodec {propertyName} => All[\"{codec.Name}\"];")
                .Line();
        }

        builder.Line()
            .Indent()
            .Append("internal static ConcurrentDictionary<string, DamageTypeCodec> All { get; } = new();")
            .Line();

        builder.EndScope();
    }
}
