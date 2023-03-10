using Microsoft.CodeAnalysis.CSharp;
using Obsidian.SourceGenerators.Registry.Models;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Xml.Schema;

namespace Obsidian.SourceGenerators.Registry;

[Generator]
public sealed partial class EntityGenerator : IIncrementalGenerator
{
    private const string AttributeName = "MinecraftEntityAttribute";
    private const string CleanedAttributeName = "MinecraftEntity";

    public void Initialize(IncrementalGeneratorInitializationContext ctx)
    {
        var jsonFiles = ctx.AdditionalTextsProvider
            .Where(file => Path.GetFileNameWithoutExtension(file.Path) == "entities")
            .Select(static (file, ct) => file.GetText(ct)!.ToString());

        IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations = ctx.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax syntax,
                static (context, _) => TranformData(context.Node as ClassDeclarationSyntax, context))
            .Where(static m => m is not null)!;

        var compilation = ctx.CompilationProvider.Combine(classDeclarations.Collect()).Combine(jsonFiles.Collect());

        ctx.RegisterSourceOutput(compilation,
            (spc, src) => this.Generate(spc, src.Left.Left, src.Left.Right, src.Right.First()));
    }

    private static ClassDeclarationSyntax? TranformData(ClassDeclarationSyntax? syntax, GeneratorSyntaxContext ctx)
    {
        if (syntax is null)
            return null;

        var symbol = ctx.SemanticModel.GetDeclaredSymbol(ctx.Node);

        if (symbol == null)
            return null;

        return symbol.GetAttributes().Any(x => x.AttributeClass?.Name == AttributeName) ? syntax : null;
    }

    private void Generate(SourceProductionContext context, Compilation compilation, ImmutableArray<ClassDeclarationSyntax> typeList, string entitiesJson)
    {
        using var document = JsonDocument.Parse(entitiesJson);

        var elements = document.RootElement;

        var asm = compilation.AssemblyName;

        if (asm == "Obsidian")
        {
            var classes = new List<EntityClass>();

            foreach (var @class in typeList)
            {
                var model = compilation.GetSemanticModel(@class.SyntaxTree);
                var symbol = model.GetDeclaredSymbol(@class);

                if (symbol is null)
                    continue;

                var attribute = @class.AttributeLists.SelectMany(x => x.Attributes).FirstOrDefault(x => x.Name.ToString() == CleanedAttributeName);

                if (attribute is null)
                    continue;

                if (!@class.Modifiers.Any(x => x.IsKind(SyntaxKind.PartialKeyword)))
                {
                    context.ReportDiagnostic(DiagnosticSeverity.Error, $"Type {symbol.Name} must be marked as partial in order to generate required properties.", @class);
                    continue;
                }

                var arg = attribute.ArgumentList!.Arguments[0];
                var expression = arg.Expression;
                var value = model.GetConstantValue(expression).ToString();

                classes.Add(new EntityClass(symbol, value));
            }

            this.GenerateClasses(classes, document, context);
        }
    }

    private void GenerateClasses(List<EntityClass> classes, JsonDocument document, SourceProductionContext context)
    {
        var element = document.RootElement;

        foreach (var @class in classes)
        {
            if (!element.TryGetProperty(@class.EntityResourceLocation, out var entityElement))
            {
                context.ReportDiagnostic(DiagnosticSeverity.Warning, $"Failed to find valid entity {@class.EntityResourceLocation}");
                continue;
            }
            var builder = new CodeBuilder()
                .Namespace("Obsidian.Entities")
                .Line()
                .Type($"public partial class {@class.Symbol.Name}");

            builder.Indent().Append("public override EntityDimension Dimension { get; protected set; } = new() { ")
                .Append($"Width = {entityElement.GetProperty("width").GetSingle()}f, ")
                .Append($"Height = {entityElement.GetProperty("height").GetSingle()}f }}; ")
                .Line()
                .Line();

            builder.EndScope();

            context.AddSource($"{@class.Symbol.Name}.g.cs", builder.ToString());
        }
    }
}
