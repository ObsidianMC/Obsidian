﻿using Obsidian.SourceGenerators.Registry.Models;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;

namespace Obsidian.SourceGenerators.Registry;

[Generator]
public sealed partial class BlocksGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        //if (!Debugger.IsAttached)
        //    Debugger.Launch();

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

        if (asm == "Obsidian.API")
        {
            GenerateBlocksProperties(context);

            CreateBlockStates(assets.Blocks, context);
            CreateStateBuilders(assets.Blocks, context);
        }
        else if (asm == "Obsidian")
        {
            GenerateBlocks(assets.Blocks, context);
        }
    }
}
