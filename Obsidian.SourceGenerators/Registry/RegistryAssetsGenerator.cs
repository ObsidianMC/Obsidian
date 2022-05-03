using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;

[Generator]
public sealed partial class RegistryAssetsGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        string? assembly = context.Compilation.AssemblyName;
        var assets = Assets.Get(context);

        if (assembly == "Obsidian")
        {
            GenerateTags(assets, context);
            GenerateItems(assets, context);
        }
        else if (assembly == "Obsidian.API")
        {
            GenerateMaterials(assets, context);
        }
    }
}
