using Obsidian.SourceGenerators.Registry.Models;
using System.Collections.Immutable;
using System.IO;

namespace Obsidian.SourceGenerators.Registry;
public sealed partial class WorldgenNoiseRegistryGenerator : IIncrementalGenerator
{
    private const string AttributeName = "DensityFunctionAttribute";
    private const string CleanedAttributeName = "DensityFunction";
    private const string DensityFunctionName = "IDensityFunction";

    public void Initialize(IncrementalGeneratorInitializationContext ctx)
    {
        //if (!Debugger.IsAttached)
        //    Debugger.Launch();

        var jsonFiles = ctx.AdditionalTextsProvider
           .Where(file => file.Path.Contains("noise_settings") && file.Path.EndsWith(".json"))
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

        if (symbol.GetAttributes().Any(x => x.AttributeClass?.Name == AttributeName))
        {
            return syntax;
        }

        return null;
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
            .Using("Obsidian.API.World.Generator")
            .Using("Obsidian.API.World.Generator.DensityFunctions")
            .Using("Obsidian.API.World.Generator.SurfaceConditions")
            .Using("Obsidian.API.World.Generator.Noise")
            .Using("Obsidian.API.World.Generator.SurfaceRules")
            .Using("Obsidian.Providers.BlockStateProviders")
            .Using("Obsidian.WorldData.BlockPredicates")
            .Using("System.Collections.Frozen")
            .Namespace("Obsidian.Registries.Noise")
            .Line()
            .Type("public static class NoiseRegistry");

        var densityFunctionTypes = new Dictionary<string, TypeInformation>();

        foreach (var @class in classes)
        {
            densityFunctionTypes.Add(@class.ResourceLocation, @class);
        }

        //BuildTreeType(densityFunctionTypes, baseFeatures, features, builder);

        builder.EndScope();

        context.AddSource("NoiseRegistry.g.cs", builder.ToString());
    }

}
