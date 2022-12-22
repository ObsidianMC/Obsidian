using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;

[Generator]
public sealed partial class BlockGenerator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        string? assembly = context.Compilation.AssemblyName;

        var assets = Assets.Get(context);

        if (assembly == "Obsidian.API")
        {
            GenerateBlocksProperties(context);

            CreateBlockStates(assets.Blocks, context);
            CreateStateBuilders(assets.Blocks, context);
        }
        else if (assembly == "Obsidian")
        {
            GenerateBlocks(assets.Blocks, context);
        }
    }

    public void Initialize(GeneratorInitializationContext context) { }
}
