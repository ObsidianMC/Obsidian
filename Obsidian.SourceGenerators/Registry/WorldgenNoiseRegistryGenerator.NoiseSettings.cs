using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;
public partial class WorldgenNoiseRegistryGenerator
{
    private static void BuildNoiseSettings(Dictionary<string, TypeInformation> densityFunctionTypes, Features features,
        CodeBuilder builder)
    {
        builder.Type("public static NoiseSettings");

        builder.EndScope();
    }
}
