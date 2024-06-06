using Microsoft.CodeAnalysis.CSharp;
using Obsidian.SourceGenerators.Registry.Models;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Obsidian.SourceGenerators.Registry;

[Generator]
public sealed partial class WorldgenNoiseRegistryGenerator : IIncrementalGenerator
{
    private const string AttributeName = "DensityFunctionAttribute";
    private const string CleanedAttributeName = "DensityFunction";

    private static readonly int WorldGenLength = "worldgen".Length;

    public void Initialize(IncrementalGeneratorInitializationContext ctx)
    {
        //if (!Debugger.IsAttached)
        //    Debugger.Launch();

        var jsonFiles = ctx.AdditionalTextsProvider
           .Where(file => file.Path.Contains("worldgen") && file.Path.EndsWith(".json"))
           .Select(static (file, ct) =>
           {
               var index = file.Path.IndexOf("worldgen");

               var name = file.Path.Substring(index + WorldGenLength + 1).Replace(".json", "");
               var content = file.GetText(ct)!.ToString();

               return (name, content);
           });

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

        var noises = Noises.Get(files);

        var classes = new List<TypeInformation>();

        if (asm != "Obsidian.API")
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

        this.GenerateClasses(classes, context, noises);
    }

    private void GenerateClasses(List<TypeInformation> classes, SourceProductionContext context, Noises noises)
    {
        var densityFunctionTypes = new Dictionary<string, TypeInformation>();
        var staticDensityFunctions = new Dictionary<string, string>();
        var noiseTypes = new Dictionary<string, string>();
        

        foreach (var @class in classes)
        {
            densityFunctionTypes.Add(@class.ResourceLocation, @class);
        }

        foreach (var func in noises.DensityFunctions)
        {
            var identifier = $"minecraft:{func.Name.Replace(DensityFunction, string.Empty).Replace("\\", "/")}";
            var split = func.Name.Split('\\');
            var list = new List<string>();

            foreach (var item in split)
                list.Add(item.ToPascalCase());

            var callableName = string.Join(".", list);

            staticDensityFunctions.Add(identifier, callableName);
        }

        foreach (var noise in noises.Noise)
        {
            var cleanedName = noise.Name.Replace(Noise, string.Empty).ToPascalCase();
            var identifier = $"minecraft:{noise.Name.Replace(Noise, string.Empty)}";

            var callableName = $"Noises.{cleanedName}";

            noiseTypes.Add(identifier, callableName);
        }

        var cleanedNoises = new CleanedNoises(densityFunctionTypes, staticDensityFunctions, noiseTypes);

        InitSection("Noises", context, (CodeBuilder builder) => BuildNoise(cleanedNoises, noises, builder));
        InitSection("DensityFunctions", context, (CodeBuilder builder) => BuildDensityFunctions(cleanedNoises, noises, builder));
        InitSection("Base", context, (CodeBuilder builder) => BuildNoiseSettings(cleanedNoises, noises, builder));
    }

    private static void InitSection(string sectionName, SourceProductionContext context, Action<CodeBuilder> method)
    {
        var builder = new CodeBuilder()
           .Using("Obsidian.API.World.Generator")
           .Using("Obsidian.API.World.Generator.DensityFunctions")
           .Using("Obsidian.API.World.Generator.SurfaceConditions")
           .Using("Obsidian.API.World.Generator.Noise")
           .Using("Obsidian.API.World.Generator.SurfaceRules")
           .Using("System.Collections.Frozen")
           .Namespace("Obsidian.API.Registries.Noise")
           .Line()
           .Type("public static partial class NoiseRegistry");

        method(builder);

        builder.EndScope();

        context.AddSource($"NoiseRegistry.{sectionName}.g.cs", builder.ToString());
    }
}
