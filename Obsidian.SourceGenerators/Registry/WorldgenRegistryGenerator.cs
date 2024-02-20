using Microsoft.CodeAnalysis.CSharp;
using Obsidian.SourceGenerators.Registry.Models;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;

namespace Obsidian.SourceGenerators.Registry;

[Generator]
public sealed partial class WorldgenRegistryGenerator : IIncrementalGenerator
{
    private const string AttributeName = "TreePropertyAttribute";
    private const string CleanedAttributeName = "TreeProperty";

    public void Initialize(IncrementalGeneratorInitializationContext ctx)
    {
        //if (!Debugger.IsAttached)
        //    Debugger.Launch();

        var jsonFiles = ctx.AdditionalTextsProvider
           .Where(file => file.Path.Contains("features") && file.Path.EndsWith(".json"))
           .Select(static (file, ct) => (name: Path.GetFileNameWithoutExtension(file.Path), content: file.GetText(ct)!.ToString()));

        IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations = ctx.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax syntax,
                static (context, _) => TransformData(context.Node as ClassDeclarationSyntax, context))
            .Where(static m => m is not null)!;

        var compilation = ctx.CompilationProvider.Combine(classDeclarations.Collect()).Combine(jsonFiles.Collect());

        ctx.RegisterSourceOutput(compilation,
            (spc, src) => this.Generate(spc, src.Left.Left, src.Left.Right, src.Right));
    }

    private static ClassDeclarationSyntax? TransformData(ClassDeclarationSyntax? syntax, GeneratorSyntaxContext ctx)
    {
        if (syntax is null)
            return null;

        var symbol = ctx.SemanticModel.GetDeclaredSymbol(ctx.Node);

        if (symbol == null)
            return null;

        return symbol.GetAttributes().Any(x => x.AttributeClass?.Name == AttributeName) ? syntax : null;
    }

    private void Generate(SourceProductionContext context, Compilation compilation, ImmutableArray<ClassDeclarationSyntax> typeList,
        ImmutableArray<(string name, string json)> files)
    {
        var asm = compilation.AssemblyName;

        var features = Features.Get(files);

        var classes = new List<TypeInformation>();

        if (asm != "Obsidian")
            return;

        foreach (var @class in typeList)
        {
            var model = compilation.GetSemanticModel(@class.SyntaxTree);
            var symbol = model.GetDeclaredSymbol(@class);

            if (symbol is null)
                continue;

            var attributes = @class.AttributeLists.SelectMany(x => x.Attributes).Where(x => x.Name.ToString() == CleanedAttributeName);

            if (attributes is null)
                continue;

            foreach (var attribute in attributes)
            {
                var arg = attribute.ArgumentList!.Arguments[0];
                var expression = arg.Expression;
                var value = model.GetConstantValue(expression).ToString();

                classes.Add(new TypeInformation(symbol, value));
            }
        }

        this.GenerateClasses(classes, context, features);

    }

    private void GenerateClasses(List<TypeInformation> classes, SourceProductionContext context, Features features)
    {
        var builder = new CodeBuilder()
            .Using("Obsidian.API.World.Features")
            .Using("Obsidian.API.World.Features.Tree")
            .Using("Obsidian.Providers.BlockStateProviders")
            .Using("Obsidian.Providers.IntProviders")
            .Using("Obsidian.WorldData.Features.Tree")
            .Using("Obsidian.WorldData.Features.Tree.Placers.Trunk")
            .Using("Obsidian.WorldData.Features.Tree.Placers.Foliage")
            .Using("Obsidian.WorldData.Features.Tree.Placers.Root")
            .Using("Obsidian.WorldData.BlockPredicates")
            .Namespace("Obsidian.API.Registries.ConfiguredFeatures")
            .Line()
            .Type("public static class TreeFeatureRegistry");

        var treePlacerTypes = new Dictionary<string, TypeInformation>();

        foreach (var @class in classes)
        {
            treePlacerTypes.Add(@class.ResourceLocation, @class);
        }

        BuildTreeType(treePlacerTypes, features, builder);

        //builder.Type("public static class TrunkPlacers");

        //builder.Line().Type("public static FrozenDictionary<string, Type> Values = new Dictionary<string, Type>()");
        //foreach (var kv in treePlacerTypes)
        //{
        //    builder.Line($"{{ \"{kv.Key}\", {kv.Value} }},");
        //}

        //builder.EndScope(".ToFrozenDictionary()", true);

        builder.EndScope().EndScope();

        context.AddSource("TreesRegistry.g.cs", builder.ToString());
    }
}
