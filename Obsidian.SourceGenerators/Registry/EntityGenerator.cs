using Microsoft.CodeAnalysis.CSharp;
using Obsidian.SourceGenerators.Registry.Models;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
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

    private void Generate(SourceProductionContext context, Compilation compilation, ImmutableArray<ClassDeclarationSyntax> typeList, string? entitiesJson)
    {
        if (entitiesJson == null) return;

        using var document = JsonDocument.Parse(entitiesJson);

        var elements = document.RootElement;

        var asm = compilation.AssemblyName;

        if (asm != "Obsidian")
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
            var value = model.GetConstantValue(expression).ToString();

            classes.Add(new TypeInformation(symbol, value));
        }

        this.GenerateClasses(classes, document, context);

    }

    private void GenerateClasses(List<TypeInformation> classes, JsonDocument document, SourceProductionContext context)
    {
        var element = document.RootElement;

        foreach (var @class in classes)
        {
            if (!element.TryGetProperty(@class.ResourceLocation, out var entityElement))
            {
                context.ReportDiagnostic(DiagnosticSeverity.Warning, $"Failed to find valid entity {@class.ResourceLocation}");
                continue;
            }

            if (!entityElement.TryGetProperty("width", out var widthElement))
            {
                context.ReportDiagnostic(DiagnosticSeverity.Error, $"Failed to find valid width for {@class.ResourceLocation}");
                continue;
            }

            if (!entityElement.TryGetProperty("height", out var heightElement))
            {
                context.ReportDiagnostic(DiagnosticSeverity.Error, $"Failed to find valid height for {@class.ResourceLocation}");
                continue;
            }

            var builder = new CodeBuilder()
                .Namespace("Obsidian.Entities")
                .Line()
                .Type($"public partial class {@class.Symbol.Name}");

            builder.Indent().Append("public override EntityDimension Dimension { get; protected set; } = new() { ")
                .Append($"Width = {widthElement.GetSingle().ToString(CultureInfo.InvariantCulture)}f, ")
                .Append($"Height = {heightElement.GetSingle().ToString(CultureInfo.InvariantCulture)}f }}; ")
                .Line()
                .Line();

            if (entityElement.TryGetProperty("is_fire_immune", out var fireImmuneElement))
            {
                builder.Indent().Append($"public override bool IsFireImmune {{ get; set; }} = {fireImmuneElement.GetBoolean().ToString().ToLowerInvariant()};")
                    .Line()
                    .Line();
            }

            if (entityElement.TryGetProperty("summonable", out var summonableElement))
            {
                builder.Indent().Append($"public override bool Summonable {{ get; set; }} = {summonableElement.GetBoolean().ToString().ToLowerInvariant()};")
                    .Line()
                    .Line();
            }

            if (entityElement.TryGetProperty("translation_key", out var translationKeyElement))
            {
                builder.Indent().Append($"public override string TranslationKey {{ get; protected set; }} = \"{translationKeyElement.GetString()}\";")
                   .Line()
                   .Line();
            }

            if (entityElement.TryGetProperty("attributes", out var attributesElement))
            {
                builder.Method("protected override ConcurrentDictionary<string, float> Attributes { get; } = new(new Dictionary<string, float>");

                foreach (var attrElement in attributesElement.EnumerateObject())
                {
                    var name = attrElement.Name;
                    var value = attrElement.Value.GetSingle().ToString(CultureInfo.InvariantCulture);

                    builder.Line($"{{ \"{name}\", {value}f }}, ");
                }

                builder.EndScope(")", true);
            }

            builder.EndScope();

            context.AddSource($"{@class.Symbol.Name}.g.cs", builder.ToString());
        }
    }
}
