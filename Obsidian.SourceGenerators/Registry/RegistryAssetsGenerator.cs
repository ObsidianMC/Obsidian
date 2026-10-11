using Obsidian.SourceGenerators.Registry.Models;
using System.Collections.Immutable;
using System.IO;

namespace Obsidian.SourceGenerators.Registry;

[Generator]
public sealed partial class RegistryAssetsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var jsonFiles = context.AdditionalTextsProvider
            .Where(file => file.Path.EndsWith(".json"))
            .Select(static (file, ct) => (name: Path.GetFileNameWithoutExtension(file.Path), content: file.GetText(ct)!.ToString()));

        var compilation = context.CompilationProvider.Combine(jsonFiles.Collect());

        context.RegisterSourceOutput(compilation, Generate);
    }

    private void Generate(SourceProductionContext context, (Compilation compilation, ImmutableArray<(string name, string json)> files) output)
    {
        var assembly = output.compilation.AssemblyName;

        // Only Obsidian's own projects have vanilla's assets; a plugin's JSON files are its own.
        if (assembly is not ("Obsidian" or "Obsidian.API"))
            return;

        var assets = Assets.Get(output.files, context);

        if (assembly == "Obsidian")
        {
            GenerateBlockIds(assets, context);
        }
        else if (assembly == "Obsidian.API")
        {
            GenerateTags(assets, context);
            GenerateItems(assets, context);
            GenerateBlockIdConstants(assets, context);
            GenerateCodecs(assets, context);
            GenerateMaterials(assets, context);
            GenerateSounds(assets, context);
            GenerateEnum(output.files.GetJsonFromArray("enums"), context);
        }
    }
}
