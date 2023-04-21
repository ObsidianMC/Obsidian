using Obsidian.SourceGenerators.Registry.Models;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;
public partial class RegistryAssetsGenerator
{
    private static void GenerateDamageTypes(Codec[] damageTypes, CodeBuilder builder, SourceProductionContext ctx)
    {
        builder.Type($"public static class DamageTypes");

        builder.Indent().Append("public const string CodecKey = \"minecraft:damage_type\";").Line().Line();

        foreach (var damageType in damageTypes)
        {
            var propertyName = damageType.Name.RemoveNamespace().ToPascalCase();
            builder.Indent().Append($"public static DamageTypeCodec {propertyName} {{ get; }} = new() {{ Id = {damageType.RegistryId}, Name = \"{damageType.Name}\", Element = new() {{ ");

            foreach (var property in damageType.Properties)
            {
                var name = property.Key;
                var value = property.Value;

                builder.Append($"{name} = ");

                if (value.ValueKind == JsonValueKind.String && name != "MessageId")
                {
                    if (name == "Scaling")
                        name = "DamageScaling";
                    else if (name == "Effects")
                        name = "DamageEffects";

                    builder.Append($"{name}.{value.GetString()!.ToPascalCase()}, ");
                }
                else
                {
                    AppendValueType(builder, value, ctx);
                }

                
            }

            builder.Append("} };").Line();
        }

        builder.Line().Statement("public static IReadOnlyDictionary<string, DamageTypeCodec> All { get; } = new Dictionary<string, DamageTypeCodec>");

        foreach (var name in damageTypes.Select(x => x.Name))
        {
            var propertyName = name.RemoveNamespace().ToPascalCase();
            builder.Line($"{{ \"{name}\", {propertyName} }},");
        }

        builder.EndScope(".AsReadOnly()", true).Line();

        builder.EndScope();
    }
}
