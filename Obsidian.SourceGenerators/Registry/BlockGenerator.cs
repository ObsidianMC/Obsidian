using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;

[Generator]
public sealed partial class BlockGenerator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        string? assembly = context.Compilation.AssemblyName;
        var assets = Assets.Get(context);

        if (assembly == "Obsidian")
        {
          
        }
        else if (assembly == "Obsidian.API")
        {
            GenerateBlocks(assets.Blocks, context);
            GenerateBlocksProperties(context);
        }
    }

    public void Initialize(GeneratorInitializationContext context) { }
}
