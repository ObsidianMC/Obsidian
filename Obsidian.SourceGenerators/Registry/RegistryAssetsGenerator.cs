using Obsidian.SourceGenerators.Registry.Models;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;

[Generator]
public sealed partial class RegistryAssetsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        //if (!Debugger.IsAttached)
        //    Debugger.Launch();

        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        var jsonFiles = context.AdditionalTextsProvider
            .Where(file => file.Path.EndsWith(".json"))
            .Select(static (file, ct) => (name: Path.GetFileNameWithoutExtension(file.Path), content: file.GetText(ct)!.ToString()));

        var compilation = context.CompilationProvider.Combine(jsonFiles.Collect());

        context.RegisterSourceOutput(compilation, this.Generate);
    }

    private void Generate(SourceProductionContext context, (Compilation compilation, ImmutableArray<(string name, string json)> files) output)
    {
        var asm = output.compilation.AssemblyName;

        var assets = Assets.Get(output.files, context);

        if (asm == "Obsidian")
        {
            GenerateTags(assets, context);
            GenerateItems(assets, context);
            GenerateBlockIds(assets, context);
            GenerateCodecs(assets, context);
        }
        else if (asm == "Obsidian.API")
        {
            GenerateMaterials(assets, context);
        }
    }
}
