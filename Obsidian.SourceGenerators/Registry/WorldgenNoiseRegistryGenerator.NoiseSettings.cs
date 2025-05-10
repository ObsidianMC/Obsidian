using Obsidian.SourceGenerators.Registry.Models;
using static Obsidian.SourceGenerators.Constants;

namespace Obsidian.SourceGenerators.Registry;
public partial class WorldgenNoiseRegistryGenerator
{
    private static void BuildNoiseSettings(CleanedNoises cleanedNoises,
        Noises noises, CodeBuilder builder)
    {
        var settings = noises.Settings;

        builder.Type("public static class NoiseSettings");

        foreach (var setting in settings)
        {
            var sanitizedName = setting.Name.Replace(NoiseSettings, string.Empty).ToPascalCase();
            builder.Type($"public static NoiseSetting {sanitizedName} => new()");

            foreach (var property in setting.Properties)
            {
                var elementName = property.Name;
                var element = property.Value;

                AppendChildProperty(cleanedNoises, elementName, element, builder, true, elementName == "noise_router");
            }

            builder.EndScope(true);
        }

        builder.Type("public static readonly FrozenDictionary<string, NoiseSetting> All = new Dictionary<string, NoiseSetting>()");

        foreach (var setting in settings)
        {
            var cleanedName = setting.Name.Replace(NoiseSettings, string.Empty);
            var sanitizedName = cleanedName.ToPascalCase();

            builder.Line($"{{ \"minecraft:{cleanedName}\", {sanitizedName}}}, ");
        }

        builder.EndScope(".ToFrozenDictionary()", true);

        builder.EndScope();
    }
}
