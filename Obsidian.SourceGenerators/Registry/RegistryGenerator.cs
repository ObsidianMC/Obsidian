using Microsoft.CodeAnalysis.CSharp;
using Obsidian.SourceGenerators.Registry.Models;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;

[Generator]
public sealed partial class RegistryGenerator : IIncrementalGenerator
{
    private const string AttributeName = "ArgumentParserAttribute";
    private const string CleanedAttributeName = "ArgumentParser";

    public void Initialize(IncrementalGeneratorInitializationContext ctx)
    {
        //if (!Debugger.IsAttached)
        //    Debugger.Launch();

        var jsonFiles = ctx.AdditionalTextsProvider
            .Where(file => Path.GetFileNameWithoutExtension(file.Path) == "command_parsers")
            .Select(static (file, ct) => file.GetText(ct)!.ToString());

        IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations = ctx.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax syntax,
                static (context, _) => TransformData(context.Node as ClassDeclarationSyntax, context))
            .Where(static m => m is not null)!;

        var compilation = ctx.CompilationProvider.Combine(classDeclarations.Collect()).Combine(jsonFiles.Collect());

        ctx.RegisterSourceOutput(compilation,
            (spc, src) => this.Generate(spc, src.Left.Left, src.Left.Right, src.Right.FirstOrDefault()));
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
        string? parsersJson)
    {
        if (parsersJson == null) return;

        using var document = JsonDocument.Parse(parsersJson);

        var elements = document.RootElement;

        var asm = compilation.AssemblyName;

        if (asm != "Obsidian.API")
            return;

        var classes = new List<TypeInformation>();

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
            var constantValue = model.GetConstantValue(expression);

            if(!constantValue.HasValue)
            {
                context.ReportDiagnostic(DiagnosticSeverity.Error, $"ArgumentParserAttribute for type {symbol.Name} must be a constant value.", @class);
                continue;
            }

            classes.Add(new TypeInformation(symbol, constantValue.Value!.ToString()));
        }

        this.GenerateClasses(classes, document, context);

    }

    private void GenerateClasses(List<TypeInformation> classes, JsonDocument document, SourceProductionContext context)
    {
        var element = document.RootElement;

        foreach (var @class in classes)
        {
            if (!element.TryGetProperty(@class.ResourceLocation, out var parser))
            {
                context.ReportDiagnostic(DiagnosticSeverity.Warning, $"Failed to find valid parser {@class.ResourceLocation}");
                continue;
            }

            var builder = new CodeBuilder();
            builder.Line();
            builder.Namespace("Obsidian.API.Commands.ArgumentParsers");
            builder.Line();
            builder.Type($"public partial class {@class.Symbol.Name}");

            builder.Line($"public override string Identifier => {SymbolDisplay.FormatLiteral(@class.ResourceLocation, true)};");
            builder.Line($"public override int Id => {parser.GetInt32()};");

            builder.EndScope();

            context.AddSource($"{@class.Symbol.Name}.g.cs", builder.ToString());
        }
    }
}
