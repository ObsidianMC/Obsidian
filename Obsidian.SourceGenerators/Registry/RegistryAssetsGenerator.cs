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
        var assets = Assets.Get(context);
        GenerateTags(assets, context);
    }
}
