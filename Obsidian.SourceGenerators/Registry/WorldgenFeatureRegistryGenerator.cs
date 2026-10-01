using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;

/// <summary>
/// Generates the <c>ConfiguredFeatures</c>, <c>PlacedFeatures</c> and <c>BiomeFeatures</c> registries from vanilla's
/// feature data in <c>Assets/worldgen/features</c>, <c>Assets/worldgen/placed_features</c> and
/// <c>Assets/worldgen/biome_features.json</c>.
/// </summary>
/// <remarks>
/// Each JSON value is emitted according to the C# type of the property it is assigned to. Polymorphic values
/// (<c>type</c> or <c>predicate_type</c>) map to the class tagged with that resource location
/// (<c>[ConfiguredFeatureClass]</c>, <c>[ConfiguredFeatureProperty]</c>, <c>[TreeProperty]</c>) that is assignable to the
/// property's type. Every registry member is created lazily and shared, so references between features are safe.
/// </remarks>
[Generator]
public sealed partial class WorldgenFeatureRegistryGenerator : IIncrementalGenerator
{
    private static readonly string[] attributeNames =
        ["ConfiguredFeatureClassAttribute", "ConfiguredFeaturePropertyAttribute", "TreePropertyAttribute", "ConfiguredFeatureAttribute"];

    private static readonly DiagnosticDescriptor unknownType = new("OBSWG001", "Unknown worldgen type",
        "No class is registered for worldgen type '{0}' assignable to {1} (used by {2})", "WorldgenFeatures", DiagnosticSeverity.Warning, true);

    private static readonly DiagnosticDescriptor unknownProperty = new("OBSWG002", "Unknown worldgen property",
        "{0} has no property '{1}' for JSON key '{2}' (used by {3})", "WorldgenFeatures", DiagnosticSeverity.Warning, true);

    private static readonly DiagnosticDescriptor unknownReference = new("OBSWG003", "Unknown feature reference",
        "Unknown {0} '{1}' (used by {2})", "WorldgenFeatures", DiagnosticSeverity.Error, true);

    public void Initialize(IncrementalGeneratorInitializationContext ctx)
    {
        var jsonFiles = ctx.AdditionalTextsProvider
            .Select(static (file, ct) => (path: GetWorldgenPath(file.Path), text: file))
            .Where(static file => file.path is not null && (file.path.StartsWith("features/") || file.path.StartsWith("placed_features/") || file.path == "biome_features"))
            .Select(static (file, ct) => (path: file.path!, json: file.text.GetText(ct)!.ToString()));

        var classDeclarations = ctx.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
                static (context, _) => (ClassDeclarationSyntax)context.Node)
            .Collect();

        var inputs = ctx.CompilationProvider.Combine(classDeclarations).Combine(jsonFiles.Collect());

        ctx.RegisterSourceOutput(inputs, static (spc, source) => Generate(spc, source.Left.Left, source.Left.Right, source.Right));
    }

    /// <summary>
    /// Path relative to <c>Assets/worldgen/</c> without extension (e.g. <c>features/trees</c>), or null.
    /// </summary>
    private static string? GetWorldgenPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        var index = normalized.IndexOf("/worldgen/", StringComparison.Ordinal);
        if (index < 0 || !normalized.EndsWith(".json", StringComparison.Ordinal))
            return null;

        var relative = normalized.Substring(index + "/worldgen/".Length);
        return relative.Substring(0, relative.Length - ".json".Length);
    }

    private static void Generate(SourceProductionContext context, Compilation compilation, ImmutableArray<ClassDeclarationSyntax> classes,
        ImmutableArray<(string path, string json)> files)
    {
        if (compilation.AssemblyName != "Obsidian" || files.IsDefaultOrEmpty)
            return;

        var types = new Dictionary<string, List<INamedTypeSymbol>>();
        foreach (var declaration in classes)
        {
            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol)
                continue;

            foreach (var attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass is null || !attributeNames.Contains(attribute.AttributeClass.Name))
                    continue;

                if (attribute.ConstructorArguments.Length == 0 || attribute.ConstructorArguments[0].Value is not string resourceLocation)
                    continue;

                if (!types.TryGetValue(resourceLocation, out var list))
                    types[resourceLocation] = list = [];

                if (!list.Contains(symbol, SymbolEqualityComparer.Default))
                    list.Add(symbol);
            }
        }

        var configured = ParseCategories(files, "features/");
        var placed = ParseCategories(files, "placed_features/");
        var biomeFeaturesJson = files.FirstOrDefault(file => file.path == "biome_features").json;

        var emitter = new FeatureEmitter(compilation, types, configured, placed, context);

        context.AddSource("ConfiguredFeatures.g.cs", emitter.EmitConfiguredFeatures());
        context.AddSource("PlacedFeatures.g.cs", emitter.EmitPlacedFeatures());

        if (biomeFeaturesJson is not null)
            context.AddSource("BiomeFeatures.g.cs", emitter.EmitBiomeFeatures(biomeFeaturesJson));
    }

    /// <summary>
    /// Category name (file name) to ordered (id, json) entries.
    /// </summary>
    private static List<(string Category, List<(string Id, JsonElement Json)> Entries)> ParseCategories(
        ImmutableArray<(string path, string json)> files, string prefix)
    {
        var result = new List<(string, List<(string, JsonElement)>)>();

        foreach (var (path, json) in files.Where(file => file.path.StartsWith(prefix)).OrderBy(file => file.path, StringComparer.Ordinal))
        {
            var document = JsonDocument.Parse(json);
            var entries = document.RootElement.EnumerateObject().Select(property => (property.Name, property.Value.Clone())).ToList();
            result.Add((path.Substring(prefix.Length), entries));
        }

        return result;
    }

    private sealed class FeatureEmitter
    {
        private readonly Compilation compilation;
        private readonly Dictionary<string, List<INamedTypeSymbol>> types;
        private readonly List<(string Category, List<(string Id, JsonElement Json)> Entries)> configured;
        private readonly List<(string Category, List<(string Id, JsonElement Json)> Entries)> placed;
        private readonly Dictionary<string, string> configuredReferences = [];
        private readonly Dictionary<string, string> placedReferences = [];
        private readonly SourceProductionContext context;

        private readonly INamedTypeSymbol configuredFeatureBase;
        private readonly INamedTypeSymbol placedFeature;
        private readonly INamedTypeSymbol placementModifierBase;
        private readonly INamedTypeSymbol heightProvider;

        private string currentOwner = string.Empty;

        public FeatureEmitter(Compilation compilation, Dictionary<string, List<INamedTypeSymbol>> types,
            List<(string, List<(string, JsonElement)>)> configured, List<(string, List<(string, JsonElement)>)> placed,
            SourceProductionContext context)
        {
            this.compilation = compilation;
            this.types = types;
            this.configured = configured;
            this.placed = placed;
            this.context = context;

            this.configuredFeatureBase = compilation.GetTypeByMetadataName("Obsidian.API.World.Features.ConfiguredFeatureBase")!;
            this.placedFeature = compilation.GetTypeByMetadataName("Obsidian.API.World.Features.PlacedFeature")!;
            this.placementModifierBase = compilation.GetTypeByMetadataName("Obsidian.API.World.Features.PlacementModifierBase")!;
            this.heightProvider = compilation.GetTypeByMetadataName("Obsidian.API.World.Features.IHeightProvider")!;

            foreach (var (category, entries) in configured)
            {
                foreach (var (id, _) in entries)
                    this.configuredReferences[id] = $"global::Obsidian.Registries.ConfiguredFeatures.{MemberName(category)}.{MemberName(id)}";
            }

            foreach (var (category, entries) in placed)
            {
                foreach (var (id, _) in entries)
                    this.placedReferences[id] = $"global::Obsidian.Registries.PlacedFeatures.{MemberName(category)}.{MemberName(id)}";
            }
        }

        public string EmitConfiguredFeatures()
        {
            var builder = Header();
            builder.AppendLine("/// <summary>");
            builder.AppendLine("/// Vanilla configured features, generated from <c>Assets/worldgen/features</c>.");
            builder.AppendLine("/// </summary>");
            builder.AppendLine("public static partial class ConfiguredFeatures");
            builder.AppendLine("{");

            foreach (var (category, entries) in this.configured)
            {
                builder.AppendLine($"    public static partial class {MemberName(category)}");
                builder.AppendLine("    {");

                foreach (var (id, json) in entries)
                {
                    this.currentOwner = id;
                    var (typeName, expression) = this.EmitConfiguredFeature(json, id);
                    builder.AppendLine($"        public static {typeName} {MemberName(id)} => {LazyInit}{expression});");
                    builder.AppendLine();
                }

                builder.AppendLine("    }");
                builder.AppendLine();
            }

            AppendAll(builder, "global::Obsidian.API.World.Features.ConfiguredFeatureBase", this.configuredReferences);
            builder.AppendLine("}");
            return builder.ToString();
        }

        public string EmitPlacedFeatures()
        {
            var builder = Header();
            builder.AppendLine("/// <summary>");
            builder.AppendLine("/// Vanilla placed features, generated from <c>Assets/worldgen/placed_features</c>.");
            builder.AppendLine("/// </summary>");
            builder.AppendLine("public static partial class PlacedFeatures");
            builder.AppendLine("{");

            foreach (var (category, entries) in this.placed)
            {
                builder.AppendLine($"    public static partial class {MemberName(category)}");
                builder.AppendLine("    {");

                foreach (var (id, json) in entries)
                {
                    this.currentOwner = id;
                    builder.AppendLine($"        public static global::Obsidian.API.World.Features.PlacedFeature {MemberName(id)} => {LazyInit}{this.EmitPlacedFeature(json, id)});");
                    builder.AppendLine();
                }

                builder.AppendLine("    }");
                builder.AppendLine();
            }

            AppendAll(builder, "global::Obsidian.API.World.Features.PlacedFeature", this.placedReferences);
            builder.AppendLine("}");
            return builder.ToString();
        }

        public string EmitBiomeFeatures(string json)
        {
            var builder = Header();
            builder.AppendLine("/// <summary>");
            builder.AppendLine("/// The placed features of each biome, per decoration step, generated from <c>Assets/worldgen/biome_features.json</c>.");
            builder.AppendLine("/// </summary>");
            builder.AppendLine("public static class BiomeFeatures");
            builder.AppendLine("{");
            builder.AppendLine("    public static global::System.Collections.Frozen.FrozenDictionary<string, global::System.Collections.Generic.IReadOnlyList<global::System.Collections.Generic.IReadOnlyList<global::Obsidian.API.World.Features.PlacedFeature>>> All => " + LazyInit + "new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.IReadOnlyList<global::System.Collections.Generic.IReadOnlyList<global::Obsidian.API.World.Features.PlacedFeature>>>()");
            builder.AppendLine("    {");

            using var document = JsonDocument.Parse(json);
            foreach (var biome in document.RootElement.EnumerateObject())
            {
                this.currentOwner = biome.Name;
                var steps = biome.Value.EnumerateArray().Select(step =>
                    "new global::Obsidian.API.World.Features.PlacedFeature[] { " +
                    string.Join(", ", step.EnumerateArray().Select(feature => this.PlacedReference(feature.GetString()!))) + " }");

                builder.AppendLine($"        {{ {Literal(biome.Name)}, new global::System.Collections.Generic.IReadOnlyList<global::Obsidian.API.World.Features.PlacedFeature>[] {{ {string.Join(", ", steps)} }} }},");
            }

            builder.AppendLine("    }.ToFrozenDictionary());");
            builder.AppendLine("}");
            return builder.ToString();
        }

        // Members are created on first use. EnsureInitialized publishes a single instance even when chunks generate in
        // parallel, which matters because features are matched by reference (biome checks, decoration order).
        private const string LazyInit = "global::System.Threading.LazyInitializer.EnsureInitialized(ref field, static () => ";

        private static StringBuilder Header()
        {
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated/>");
            builder.AppendLine("using System.Collections.Frozen;");
            builder.AppendLine();
            builder.AppendLine("namespace Obsidian.Registries;");
            builder.AppendLine();
            return builder;
        }

        private static void AppendAll(StringBuilder builder, string typeName, Dictionary<string, string> references)
        {
            builder.AppendLine("    /// <summary>");
            builder.AppendLine("    /// Every entry, keyed by registry id.");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine($"    public static FrozenDictionary<string, {typeName}> All => {LazyInit}new global::System.Collections.Generic.Dictionary<string, {typeName}>()");
            builder.AppendLine("    {");

            foreach (var entry in references)
                builder.AppendLine($"        {{ {Literal(entry.Key)}, {entry.Value} }},");

            builder.AppendLine("    }.ToFrozenDictionary());");
        }

        /// <summary>
        /// Emits a configured feature (<c>{type, config}</c>); returns the member type name and the expression.
        /// </summary>
        private (string TypeName, string Expression) EmitConfiguredFeature(JsonElement json, string? identifier)
        {
            var type = json.GetProperty("type").GetString()!;
            var symbol = this.FindType(type, this.configuredFeatureBase);

            if (symbol is null)
            {
                this.Report(unknownType, type, "ConfiguredFeatureBase");
                return ("global::Obsidian.API.World.Features.ConfiguredFeatureBase", $"new global::Obsidian.WorldData.Features.UnsupportedFeature({Literal(type)})");
            }

            var config = json.TryGetProperty("config", out var value) ? value : default;
            var extra = identifier is null ? null : $"Identifier = {Literal(identifier)}";
            return (FullName(symbol), this.EmitObject(symbol, config, null, extra));
        }

        private string EmitPlacedFeature(JsonElement json, string? identifier)
        {
            var feature = json.GetProperty("feature");
            var featureExpression = feature.ValueKind == JsonValueKind.String
                ? this.ConfiguredReference(feature.GetString()!)
                : this.EmitConfiguredFeature(feature, null).Expression;

            var placement = json.TryGetProperty("placement", out var modifiers)
                ? this.EmitValue(this.compilation.CreateArrayTypeSymbol(this.placementModifierBase), modifiers)
                : "[]";

            var parts = new List<string>();
            if (identifier is not null)
                parts.Add($"Identifier = {Literal(identifier)}");

            parts.Add($"Feature = {featureExpression}");
            parts.Add($"Placement = {placement}");

            return $"new global::Obsidian.API.World.Features.PlacedFeature {{ {string.Join(", ", parts)} }}";
        }

        private string ConfiguredReference(string id)
        {
            if (this.configuredReferences.TryGetValue(id, out var reference))
                return reference;

            this.Report(unknownReference, "configured feature", id);
            return "default!";
        }

        private string PlacedReference(string id)
        {
            if (this.placedReferences.TryGetValue(id, out var reference))
                return reference;

            this.Report(unknownReference, "placed feature", id);
            return "default!";
        }

        /// <summary>
        /// Emits <paramref name="json"/> as an expression of type <paramref name="target"/>.
        /// </summary>
        private string EmitValue(ITypeSymbol target, JsonElement json)
        {
            if (target is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
                target = nullable.TypeArguments[0];

            switch (target.SpecialType)
            {
                case SpecialType.System_String:
                    return Literal(json.ValueKind == JsonValueKind.String ? json.GetString()! : json.GetRawText());
                case SpecialType.System_Boolean:
                    return json.GetBoolean() ? "true" : "false";
                case SpecialType.System_Int32:
                    return json.GetInt32().ToString(CultureInfo.InvariantCulture);
                case SpecialType.System_Int64:
                    return json.GetInt64().ToString(CultureInfo.InvariantCulture) + "L";
                case SpecialType.System_Single:
                    return json.GetSingle().ToString("R", CultureInfo.InvariantCulture) + "f";
                case SpecialType.System_Double:
                    return json.GetDouble().ToString("R", CultureInfo.InvariantCulture) + "d";
            }

            if (target.TypeKind == TypeKind.Enum)
                return this.EmitEnum((INamedTypeSymbol)target, json);

            if (target is IArrayTypeSymbol array)
                return this.EmitCollection(array.ElementType, json);

            if (target is INamedTypeSymbol { IsGenericType: true } generic && generic.TypeArguments.Length == 1 && IsCollection(generic))
                return this.EmitCollection(generic.TypeArguments[0], json);

            switch (target.Name)
            {
                case "SimpleBlockState":
                    return EmitBlockState(json);
                case "BlockSet":
                    {
                        var entries = json.ValueKind == JsonValueKind.Array ? json.EnumerateArray().Select(entry => entry.GetString()!) : [json.GetString()!];
                        return $"new global::Obsidian.WorldData.Features.BlockSet({string.Join(", ", entries.Select(Literal))})";
                    }
                case "Vector":
                    {
                        var values = json.EnumerateArray().Select(value => value.GetInt32().ToString(CultureInfo.InvariantCulture)).ToArray();
                        return $"new global::Obsidian.API.Vector({string.Join(", ", values)})";
                    }
                case "VerticalAnchor":
                    return EmitVerticalAnchor(json);
            }

            if (SymbolEqualityComparer.Default.Equals(target, this.placedFeature))
            {
                return json.ValueKind == JsonValueKind.String ? this.PlacedReference(json.GetString()!) : this.EmitPlacedFeature(json, null);
            }

            if (IsAssignableTo(target, this.configuredFeatureBase))
            {
                return json.ValueKind == JsonValueKind.String ? this.ConfiguredReference(json.GetString()!) : this.EmitConfiguredFeature(json, null).Expression;
            }

            var named = (INamedTypeSymbol)target;
            if (named.IsAbstract || named.TypeKind == TypeKind.Interface)
                return this.EmitPolymorphic(named, json);

            return this.EmitObject(named, json, null, null);
        }

        private string EmitPolymorphic(INamedTypeSymbol target, JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.Number)
            {
                var constant = this.FindType("minecraft:constant", target);
                if (constant is not null)
                {
                    var valueType = FindProperty(constant, "Value")!.Type;
                    return $"new {FullName(constant)} {{ Value = {this.EmitValue(valueType, json)} }}";
                }
            }

            if (json.ValueKind == JsonValueKind.Object)
            {
                var discriminator = json.TryGetProperty("type", out var type) ? "type" : json.TryGetProperty("predicate_type", out type) ? "predicate_type" : null;

                if (discriminator is null && SymbolEqualityComparer.Default.Equals(target, this.heightProvider))
                {
                    var constant = this.FindType("minecraft:constant", target)!;
                    return $"new {FullName(constant)} {{ Value = {EmitVerticalAnchor(json)} }}";
                }

                if (discriminator is not null)
                {
                    var name = type.GetString()!;
                    var symbol = this.FindType(name, target);
                    if (symbol is not null)
                        return this.EmitObject(symbol, json, discriminator, null);

                    this.Report(unknownType, name, target.Name);
                    return "default!";
                }
            }

            this.Report(unknownType, json.ValueKind.ToString(), target.Name);
            return "default!";
        }

        /// <summary>
        /// Emits <c>new Type { Property = value, ... }</c>, mapping JSON keys to PascalCase properties.
        /// </summary>
        private string EmitObject(INamedTypeSymbol type, JsonElement json, string? discriminator, string? extraAssignment)
        {
            var assignments = new List<string>();
            if (extraAssignment is not null)
                assignments.Add(extraAssignment);

            if (discriminator is not null && FindProperty(type, "Type") is { SetMethod: not null })
                assignments.Add($"Type = {Literal(json.GetProperty(discriminator).GetString()!)}");

            if (json.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in json.EnumerateObject())
                {
                    if (property.Name == discriminator)
                        continue;

                    var memberName = PropertyName(property.Name);
                    var member = FindProperty(type, memberName);

                    if (member is null || member.SetMethod is null)
                    {
                        this.Report(unknownProperty, type.Name, memberName, property.Name);
                        continue;
                    }

                    assignments.Add($"{memberName} = {this.EmitValue(member.Type, property.Value)}");
                }
            }

            return assignments.Count == 0 ? $"new {FullName(type)}()" : $"new {FullName(type)} {{ {string.Join(", ", assignments)} }}";
        }

        private string EmitCollection(ITypeSymbol elementType, JsonElement json)
        {
            var items = json.ValueKind == JsonValueKind.Array
                ? json.EnumerateArray().Select(item => this.EmitValue(elementType, item))
                : [this.EmitValue(elementType, json)];

            return $"[{string.Join(", ", items)}]";
        }

        private string EmitEnum(INamedTypeSymbol type, JsonElement json)
        {
            var value = Normalize(json.GetString()!);
            var member = type.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(field => field.IsConst && Normalize(field.Name) == value);

            if (member is null)
            {
                this.Report(unknownProperty, type.Name, json.GetString()!, "enum value");
                return "default";
            }

            return $"{FullName(type)}.{member.Name}";
        }

        private static string EmitBlockState(JsonElement json)
        {
            var name = Literal(json.GetProperty("Name").GetString()!);
            if (!json.TryGetProperty("Properties", out var properties) || !properties.EnumerateObject().Any())
                return $"new global::Obsidian.API.SimpleBlockState {{ Name = {name} }}";

            var entries = properties.EnumerateObject().Select(property => $"{{ {Literal(property.Name)}, {Literal(property.Value.GetString()!)} }}");
            return $"new global::Obsidian.API.SimpleBlockState {{ Name = {name}, Properties = new() {{ {string.Join(", ", entries)} }} }}";
        }

        private static string EmitVerticalAnchor(JsonElement json)
        {
            const string type = "global::Obsidian.API.World.Features.VerticalAnchor";

            if (json.TryGetProperty("absolute", out var absolute))
                return $"{type}.WithAbsolute({absolute.GetInt32()})";

            if (json.TryGetProperty("above_bottom", out var aboveBottom))
                return $"{type}.WithAboveBottom({aboveBottom.GetInt32()})";

            return $"{type}.WithBelowTop({json.GetProperty("below_top").GetInt32()})";
        }

        private INamedTypeSymbol? FindType(string resourceLocation, ITypeSymbol target) =>
            this.types.TryGetValue(resourceLocation, out var candidates)
                ? candidates.FirstOrDefault(candidate => !candidate.IsAbstract && IsAssignableTo(candidate, target))
                : null;

        private static IPropertySymbol? FindProperty(INamedTypeSymbol type, string name)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                var property = current.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault();
                if (property is not null)
                    return property;
            }

            return null;
        }

        private static bool IsAssignableTo(ITypeSymbol type, ITypeSymbol target)
        {
            if (SymbolEqualityComparer.Default.Equals(type, target))
                return true;

            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, target))
                    return true;
            }

            return type.AllInterfaces.Any(@interface => SymbolEqualityComparer.Default.Equals(@interface, target));
        }

        private static bool IsCollection(INamedTypeSymbol type) =>
            type.Name is "List" or "IReadOnlyList" or "IEnumerable" or "IReadOnlyCollection" or "ICollection" or "IList";

        private void Report(DiagnosticDescriptor descriptor, params object[] arguments) =>
            this.context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, [.. arguments, this.currentOwner]));

        private static string FullName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, true);

        private static string Normalize(string value) => value.Replace("_", string.Empty).ToLowerInvariant();

        /// <summary>
        /// JSON key to property name: <c>discard_chance_on_air_exposure</c> → <c>DiscardChanceOnAirExposure</c>,
        /// <c>firstOctave</c> → <c>FirstOctave</c>.
        /// </summary>
        private static string PropertyName(string key)
        {
            var builder = new StringBuilder();
            foreach (var part in key.Split('_'))
            {
                if (part.Length > 0)
                    builder.Append(char.ToUpperInvariant(part[0])).Append(part.Substring(1));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Registry id or category to a member name: <c>minecraft:ore_iron_small</c> → <c>OreIronSmall</c>.
        /// </summary>
        private static string MemberName(string id)
        {
            var path = id.StartsWith("minecraft:", StringComparison.Ordinal) ? id.Substring("minecraft:".Length) : id.Replace(':', '_');
            var name = PropertyName(path.Replace('/', '_'));
            return char.IsDigit(name[0]) ? "_" + name : name;
        }
    }
}
