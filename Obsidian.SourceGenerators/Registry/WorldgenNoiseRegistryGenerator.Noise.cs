using Obsidian.SourceGenerators.Registry.Models;
using static Obsidian.SourceGenerators.Constants;

namespace Obsidian.SourceGenerators.Registry;
public partial class WorldgenNoiseRegistryGenerator
{
    private static void BuildNoise(CleanedNoises cleanedNoises,
       Noises noises, CodeBuilder builder)
    {
        var noise = noises.Noise;

        builder.Type("public static class Noises");

        foreach (var value in noise)
        {
            var sanitizedName = value.Name.Replace(Noise, string.Empty).ToPascalCase();
            builder.Type($"public static BaseNoise {sanitizedName} => new()");

            foreach (var property in value.Properties)
            {
                var elementName = property.Name;
                var element = property.Value;

                AppendChildProperty(cleanedNoises, elementName, element, builder, true);
            }

            builder.EndScope(true);
        }

        builder.Type("public static readonly FrozenDictionary<string, BaseNoise> All = new Dictionary<string, BaseNoise>()");

        foreach (var value in noise)
        {
            var cleanedName = value.Name.Replace(Noise, string.Empty);
            var sanitizedName = cleanedName.ToPascalCase();

            builder.Line($"{{ \"minecraft:{cleanedName}\", {sanitizedName}}}, ");
        }

        builder.EndScope(".ToFrozenDictionary()", true);

        builder.EndScope();
    }
}
