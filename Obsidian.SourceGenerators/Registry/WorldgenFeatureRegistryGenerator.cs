using Microsoft.CodeAnalysis.CSharp;
using Obsidian.SourceGenerators.Registry.Models;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;

[Generator]
public sealed partial class WorldgenFeatureRegistryGenerator : IIncrementalGenerator
{
    private static readonly string[] attributes = [ConfiguredFeaturePropertyAttributeName, TreePropertyAttributeName, ConfiguredFeatureAttributeName];

    private const string TreePropertyAttributeName = "TreePropertyAttribute";
    private const string CleanedTreePropertyAttributeName = "TreeProperty";

    private const string ConfiguredFeaturePropertyAttributeName = "ConfiguredFeaturePropertyAttribute";
    private const string CleanedConfiguredFeaturePropertyAttributeName = "ConfiguredFeatureProperty";

    private const string ConfiguredFeatureAttributeName = "ConfiguredFeatureAttribute";
    private const string CleanedConfiguredFeatureAttributeName = "ConfiguredFeature";

    private const string IntProviderName = "IIntProvider";
    private const string HeightProviderName = "IHeightProvider";
    private const string ConfiguredFeatureBaseName = "ConfiguredFeatureBase";

    public void Initialize(IncrementalGeneratorInitializationContext ctx)
    {
        var jsonFiles = ctx.AdditionalTextsProvider
            .Where(file => file.Path.Contains("features") && file.Path.EndsWith(".json"))
            .Select(static (file, ct) => (name: Path.GetFileNameWithoutExtension(file.Path), content: file.GetText(ct)!.ToString()));

        IncrementalValuesProvider<ClassDeclarationSyntax> treePropertyClassDeclarations = ctx.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsClassDeclaration(node),
                static (context, _) => TransformPropertyData(context.Node as ClassDeclarationSyntax, context))
            .Where(static m => m is not null)!;

        var combinedInputs = ctx.CompilationProvider
            .Combine(treePropertyClassDeclarations.Collect())
            .Combine(jsonFiles.Collect())
            .Select(static (src, _) => new PipelineInputs(
                compilation: src.Left.Left,
                configuredFeatureProperties: src.Left.Right,
                jsonFiles: src.Right));

        ctx.RegisterSourceOutput(
            combinedInputs,
            (spc, inputs) => this.Generate(spc, inputs.Compilation, inputs.ConfiguredFeatureProperties, inputs.JsonFiles));
    }

    private static ClassDeclarationSyntax? TransformPropertyData(ClassDeclarationSyntax? syntax, GeneratorSyntaxContext ctx)
    {
        if (syntax is null)
            return null;

        var symbol = ctx.SemanticModel.GetDeclaredSymbol(ctx.Node);

        if (symbol == null)
            return null;

        if (symbol.GetAttributes().Any(x => attributes.Contains(x.AttributeClass?.Name)))
            return syntax;

        return null;
    }

    private void Generate(SourceProductionContext context, Compilation compilation,
        ImmutableArray<ClassDeclarationSyntax> configuredFeaturePropertiesDecs, ImmutableArray<(string name, string json)> files)
    {
        var asm = compilation.AssemblyName;

        if (asm != "Obsidian")
            return;

        var features = Features.Get(files);

        var configuredFeatureProperties = configuredFeaturePropertiesDecs.SelectMany(x => GetTypeInformation(x, compilation,
            CleanedConfiguredFeaturePropertyAttributeName,
            CleanedTreePropertyAttributeName,
            CleanedConfiguredFeatureAttributeName));

        this.GenerateClasses(configuredFeatureProperties, context, features);
    }

    private static List<TypeInformation> GetTypeInformation(ClassDeclarationSyntax @class, Compilation compilation, params string[] attributeNames)
    {
        var classes = new List<TypeInformation>();

        var model = compilation.GetSemanticModel(@class.SyntaxTree);
        var symbol = model.GetDeclaredSymbol(@class);

        if (symbol is null)
            return classes;

        var attributes = @class.AttributeLists.SelectMany(x => x.Attributes).Where(x => attributeNames.Contains(x.Name.ToString()));

        if (attributes is null)
            return classes;

        foreach (var attribute in attributes)
        {
            var arg = attribute.ArgumentList!.Arguments[0];
            var expression = arg.Expression;
            var value = model.GetConstantValue(expression).ToString();

            classes.Add(new TypeInformation(symbol, value, false));
        }

        return classes;
    }

    private void GenerateClasses(IEnumerable<TypeInformation> configuredFeaturePropertyClasses, SourceProductionContext context, Features features)
    {
        var featureTypes = new Dictionary<string, TypeInformation>();

        var baseFeatures = new BaseFeatureDictionary();
        foreach (var @class in configuredFeaturePropertyClasses)
        {
            if (@class.Symbol.Interfaces.Any(x => x.Name == IntProviderName))
            {
                baseFeatures.AddIntProvider(@class.ResourceLocation, @class);

                continue;
            }
            else if (@class.Symbol.Interfaces.Any(x => x.Name == HeightProviderName))
            {
                baseFeatures.AddHeightProvider(@class.ResourceLocation, @class);

                continue;
            }
            else if (@class.Symbol.BaseType?.Name == ConfiguredFeatureBaseName)
            {
                baseFeatures.AddConfiguredFeature(@class.ResourceLocation, @class with { IsConfiguredFeature = true });
                continue;
            }


            featureTypes.Add(@class.ResourceLocation, @class);
        }

        var builder = new CodeBuilder()
            .Using("Obsidian.API.World.Features")
            .Using("Obsidian.API.World.Features.Flower")
            .Using("Obsidian.API.World.Features.Tree")
            .Using("Obsidian.WorldData.Features.Tree")
            .Using("Obsidian.WorldData.Features.Tree.Placers.Trunk")
            .Using("Obsidian.WorldData.Features.Tree.Placers.Foliage")
            .Using("Obsidian.WorldData.Features.Tree.Placers.Root")
            .Using("Obsidian.Providers.BlockStateProviders")
            .Using("Obsidian.Providers.IntProviders")
            .Using("Obsidian.WorldData.BlockPredicates")
            .Using("Obsidian.WorldData.Features")
            .Using("System.Collections.Frozen")
            .Namespace("Obsidian.Registries")
            .Line()
            .Type("public static class ConfiguredFeatures");

        builder.Type("public static class Flowers", (classBuilder) =>
        {
            BuildType("FlowerFeature", featureTypes, baseFeatures, features.FlowerFeatures, classBuilder);
        });

        builder.Type("public static class Trees", (classBuilder) =>
        {
            BuildType("TreeFeature", featureTypes, baseFeatures, features.TreeFeatures, classBuilder);
        });

        builder.EndScope();

        context.AddSource("ConfiguredFeatures.g.cs", builder.ToString());
    }

    private static void BuildType(string name, Dictionary<string, TypeInformation> featureTypes, BaseFeatureDictionary baseFeatureTypes,
        BaseFeature[] features, CodeBuilder builder)
    {
        foreach (var feature in features)
        {
            var sanitizedName = feature.Name.ToPascalCase().RemoveNamespace();
            builder.Type($"public static readonly {name} {sanitizedName} = new()");

            builder.Line($"Identifier = {SymbolDisplay.FormatLiteral(feature.Name, true)}, ");

            foreach (var property in feature.Properties)
            {
                var elementName = property.Name;
                var element = property.Value;

                //Temp workaround :weary:
                if (elementName == "can_grow_through" && element.ValueKind != JsonValueKind.Array)
                {
                    builder.Line($"{elementName.ToPascalCase()} = {{ {SymbolDisplay.FormatLiteral(element.GetString()!, true)} }}, ");
                    continue;
                }

                ClassBuilder.AppendChildProperty(featureTypes, baseFeatureTypes, default, elementName, element, builder);
            }

            builder.EndScope(true);
        }

        builder.Type($"public static readonly FrozenDictionary<string, {name}> All = new Dictionary<string, {name}>()");

        foreach (var feature in features)
        {
            var sanitizedName = feature.Name.ToPascalCase().RemoveNamespace();

            builder.Line($"{{ \"{feature.Name}\", {sanitizedName}}}, ");
        }

        builder.EndScope(".ToFrozenDictionary()", true);
    }

    private static bool IsClassDeclaration(SyntaxNode node) => node is ClassDeclarationSyntax;

    private readonly struct PipelineInputs(
        Compilation compilation,
        ImmutableArray<ClassDeclarationSyntax> configuredFeatureProperties,
        ImmutableArray<(string name, string json)> jsonFiles)
    {
        public Compilation Compilation { get; } = compilation;
        public ImmutableArray<ClassDeclarationSyntax> ConfiguredFeatureProperties { get; } = configuredFeatureProperties;
        public ImmutableArray<(string name, string json)> JsonFiles { get; } = jsonFiles;
    }
}
