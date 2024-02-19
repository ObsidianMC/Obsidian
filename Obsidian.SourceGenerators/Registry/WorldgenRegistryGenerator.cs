using Microsoft.CodeAnalysis.CSharp;
using Obsidian.SourceGenerators.Registry.Models;
using System.Collections.Immutable;

namespace Obsidian.SourceGenerators.Registry;

[Generator]
public sealed class WorldgenRegistryGenerator : IIncrementalGenerator
{
    private const string AttributeName = "TreePropertyAttribute";
    private const string CleanedAttributeName = "TreeProperty";

    public void Initialize(IncrementalGeneratorInitializationContext ctx)
    {
        IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations = ctx.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax syntax,
                static (context, _) => TransformData(context.Node as ClassDeclarationSyntax, context))
            .Where(static m => m is not null)!;

        var compilation = ctx.CompilationProvider.Combine(classDeclarations.Collect());

        ctx.RegisterSourceOutput(compilation,
            (spc, src) => this.Generate(spc, src.Left, src.Right));
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

    private void Generate(SourceProductionContext context, Compilation compilation, ImmutableArray<ClassDeclarationSyntax> typeList)
    {
        var asm = compilation.AssemblyName;

        //Want this to work for any assembly so use anything that't not from the Obsidian assembly.
        if (asm == "Obsidian")
            return;

        var classes = new List<EntityClass>();

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

                classes.Add(new EntityClass(symbol, value));
            }
        }

        this.GenerateClasses(classes, context);

    }

    private void GenerateClasses(List<EntityClass> classes, SourceProductionContext context)
    {
        var builder = new CodeBuilder()
            .Using("Obsidian.API.World.Features.Tree.Placers.Trunk")
            .Using("System.Collections.Frozen")
            .Namespace("Obsidian.API.Registries.ConfiguredFeatures")
            .Line()
            .Type("public static class TreeFeatureRegistry");

        builder.Type("public static class TrunkPlacers");

        var treePlacerTypes = new Dictionary<string, string>();
        var added = new List<string>();
        foreach (var @class in classes.Where(x => x.Symbol.Name.EndsWith("TrunkPlacer")))
        {
            var trunkPlacer = @class.Symbol;
            if (added.Contains(trunkPlacer.Name))
            {
                treePlacerTypes.Add(@class.EntityResourceLocation, $"{trunkPlacer.Name}Type");
                continue;
            }

            builder.Indent()
            .Append($"public static Type {trunkPlacer.Name}Type {{ get; }} = typeof({@class.Symbol.Name});")
            .Line();

            treePlacerTypes.Add(@class.EntityResourceLocation, $"{trunkPlacer.Name}Type");
            added.Add(trunkPlacer.Name);
        }

        builder.Line().Type("public static FrozenDictionary<string, Type> Values = new Dictionary<string, Type>()");
        foreach (var kv in treePlacerTypes)
        {
            builder.Line($"{{ \"{kv.Key}\", {kv.Value} }},");
        }

        builder.EndScope(".ToFrozenDictionary()", true);

        builder.EndScope().EndScope();

        context.AddSource("TreesRegistry.g.cs", builder.ToString());
    }
}
