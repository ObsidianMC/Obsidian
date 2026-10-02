using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;

/// <summary>
/// Generates <c>LootTables</c> from vanilla's loot tables in <c>Assets/loot_table/*.json</c> (one file per category,
/// keyed by table id), and the <c>EnchantmentsRegistry</c> and <c>InstrumentsRegistry</c> the tables reference from
/// <c>Assets/enchantments.json</c> and <c>Assets/instruments.json</c>.
/// </summary>
/// <remarks>
/// Loot table JSON is emitted according to the C# type of the property it's assigned to. Polymorphic values map to the
/// class tagged with <c>[LootType]</c> for the JSON discriminator (<c>function</c> for functions, <c>condition</c> for
/// conditions, <c>type</c> otherwise) that is assignable to the property's type. Item, enchantment and instrument ids
/// become references to their registry members, and tags (<c>#minecraft:on_random_loot</c>) are expanded in vanilla's
/// tag order using <c>Assets/tags.json</c>.
/// </remarks>
[Generator]
public sealed class LootRegistryGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor unknownType = new("OBSLT001", "Unknown loot type",
        "No class is tagged [LootType(\"{0}\")] assignable to {1} (used by {2})", "Loot", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor unknownProperty = new("OBSLT002", "Unknown loot property",
        "{0} has no settable property '{1}' for JSON key '{2}' (used by {3})", "Loot", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor unknownReference = new("OBSLT003", "Unknown loot reference",
        "Unknown {0} '{1}' (used by {2})", "Loot", DiagnosticSeverity.Error, true);

    public void Initialize(IncrementalGeneratorInitializationContext ctx)
    {
        var jsonFiles = ctx.AdditionalTextsProvider
            .Select(static (file, ct) => (path: GetAssetPath(file.Path), text: file))
            .Where(static file => file.path is not null && (file.path.StartsWith("loot_table/", StringComparison.Ordinal) ||
                file.path is "enchantments" or "instruments" or "tags" or "items"))
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
    /// Path relative to <c>Assets/</c> without extension (e.g. <c>loot_table/chests</c>), or null.
    /// </summary>
    private static string? GetAssetPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        var index = normalized.LastIndexOf("/Assets/", StringComparison.Ordinal);
        if (index < 0 || !normalized.EndsWith(".json", StringComparison.Ordinal))
            return null;

        var relative = normalized.Substring(index + "/Assets/".Length);
        return relative.Substring(0, relative.Length - ".json".Length);
    }

    private static void Generate(SourceProductionContext context, Compilation compilation, ImmutableArray<ClassDeclarationSyntax> classes,
        ImmutableArray<(string path, string json)> files)
    {
        if (compilation.AssemblyName != "Obsidian.API")
            return;

        string? Find(string path) => files.FirstOrDefault(file => file.path == path).json;

        var enchantmentsJson = Find("enchantments");
        var instrumentsJson = Find("instruments");
        var tagsJson = Find("tags");
        var itemsJson = Find("items");
        if (enchantmentsJson is null || instrumentsJson is null || tagsJson is null || itemsJson is null)
            return;

        var types = new Dictionary<string, List<INamedTypeSymbol>>();
        foreach (var declaration in classes)
        {
            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol)
                continue;

            foreach (var attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass?.Name != "LootTypeAttribute" || attribute.ConstructorArguments.Length == 0 ||
                    attribute.ConstructorArguments[0].Value is not string resourceLocation)
                    continue;

                if (!types.TryGetValue(resourceLocation, out var list))
                    types[resourceLocation] = list = [];

                list.Add(symbol);
            }
        }

        using var enchantments = JsonDocument.Parse(enchantmentsJson);
        using var instruments = JsonDocument.Parse(instrumentsJson);
        using var tags = JsonDocument.Parse(tagsJson);
        using var items = JsonDocument.Parse(itemsJson);

        var emitter = new LootEmitter(compilation, types, enchantments.RootElement, instruments.RootElement, tags.RootElement,
            items.RootElement, context);

        context.AddSource("EnchantmentsRegistry.g.cs", emitter.EmitEnchantments());
        context.AddSource("InstrumentsRegistry.g.cs", emitter.EmitInstruments());

        var categories = files
            .Where(file => file.path.StartsWith("loot_table/", StringComparison.Ordinal))
            .OrderBy(file => file.path, StringComparer.Ordinal)
            .Select(file => (Category: file.path.Substring("loot_table/".Length), Json: file.json))
            .ToList();

        context.AddSource("LootTables.g.cs", emitter.EmitLootTables(categories));
    }

    private sealed class LootEmitter
    {
        private const string Registries = "global::Obsidian.API.Registries";

        private readonly Compilation compilation;
        private readonly Dictionary<string, List<INamedTypeSymbol>> types;
        private readonly JsonElement enchantments;
        private readonly JsonElement instruments;
        private readonly JsonElement tags;
        private readonly JsonElement items;
        private readonly SourceProductionContext context;

        private readonly Dictionary<string, int> enchantmentIds = [];
        private readonly INamedTypeSymbol lootFunction;
        private readonly INamedTypeSymbol lootCondition;
        private readonly INamedTypeSymbol numberProvider;

        private string currentOwner = string.Empty;

        public LootEmitter(Compilation compilation, Dictionary<string, List<INamedTypeSymbol>> types, JsonElement enchantments,
            JsonElement instruments, JsonElement tags, JsonElement items, SourceProductionContext context)
        {
            this.compilation = compilation;
            this.types = types;
            this.enchantments = enchantments;
            this.instruments = instruments;
            this.tags = tags;
            this.items = items;
            this.context = context;

            this.lootFunction = compilation.GetTypeByMetadataName("Obsidian.API.Loot.Functions.LootFunction")!;
            this.lootCondition = compilation.GetTypeByMetadataName("Obsidian.API.Loot.Conditions.ILootCondition")!;
            this.numberProvider = compilation.GetTypeByMetadataName("Obsidian.API.Loot.Numbers.INumberProvider")!;

            // Vanilla's enchantment registry is sorted by id; the index is the network id.
            foreach (var enchantment in enchantments.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal))
                this.enchantmentIds[enchantment] = this.enchantmentIds.Count;
        }

        public string EmitEnchantments()
        {
            var builder = Header("Obsidian.API.Registries");
            builder.AppendLine("/// <summary>");
            builder.AppendLine("/// Vanilla enchantments, generated from <c>Assets/enchantments.json</c>.");
            builder.AppendLine("/// </summary>");
            builder.AppendLine("public static class EnchantmentsRegistry");
            builder.AppendLine("{");

            var ordered = this.enchantments.EnumerateObject().OrderBy(property => this.enchantmentIds[property.Name]).ToList();
            foreach (var property in ordered)
            {
                this.currentOwner = property.Name;
                var json = property.Value;
                var parts = new List<string>
                {
                    $"Identifier = {Literal(property.Name)}",
                    $"Id = {this.enchantmentIds[property.Name]}",
                    $"SupportedItems = {this.EmitItemSet(json.GetProperty("supported_items"))}"
                };

                if (json.TryGetProperty("primary_items", out var primaryItems))
                    parts.Add($"PrimaryItems = {this.EmitItemSet(primaryItems)}");

                parts.Add($"Weight = {json.GetProperty("weight").GetInt32()}");
                parts.Add($"MaxLevel = {json.GetProperty("max_level").GetInt32()}");
                parts.Add($"MinCost = {EmitCost(json.GetProperty("min_cost"))}");
                parts.Add($"MaxCost = {EmitCost(json.GetProperty("max_cost"))}");

                if (json.TryGetProperty("exclusive_set", out var exclusiveSet))
                {
                    var ids = this.ExpandReferences("enchantment", exclusiveSet)
                        .Select(id => this.enchantmentIds.TryGetValue(id, out var value) ? value.ToString(CultureInfo.InvariantCulture) : this.Missing("enchantment", id, "0"));
                    parts.Add($"ExclusiveSet = [{string.Join(", ", ids)}]");
                }

                builder.AppendLine($"    public static global::Obsidian.API.Loot.EnchantmentDefinition {MemberName(property.Name)} {{ get; }} = new() {{ {string.Join(", ", parts)} }};");
                builder.AppendLine();
            }

            builder.AppendLine("    /// <summary>");
            builder.AppendLine("    /// Every enchantment in registry order, so an enchantment's <c>Id</c> is its index.");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine($"    public static global::System.Collections.Generic.IReadOnlyList<global::Obsidian.API.Loot.EnchantmentDefinition> All {{ get; }} = [{string.Join(", ", ordered.Select(property => MemberName(property.Name)))}];");
            builder.AppendLine("}");
            return builder.ToString();
        }

        public string EmitInstruments()
        {
            var builder = Header("Obsidian.API.Registries");
            builder.AppendLine("/// <summary>");
            builder.AppendLine("/// Vanilla instruments, generated from <c>Assets/instruments.json</c>.");
            builder.AppendLine("/// </summary>");
            builder.AppendLine("public static class InstrumentsRegistry");
            builder.AppendLine("{");

            // Like the enchantments, vanilla's instrument registry is sorted by id and the index is the network id.
            var chatMessage = this.compilation.GetTypeByMetadataName("Obsidian.API.ChatMessage")!;
            var ordered = this.instruments.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).ToList();
            for (var id = 0; id < ordered.Count; id++)
            {
                var property = ordered[id];
                this.currentOwner = property.Name;
                var json = property.Value;
                var parts = new[]
                {
                    $"Identifier = {Literal(property.Name)}",
                    $"Id = {id}",
                    $"SoundEvent = {Literal(json.GetProperty("sound_event").GetString()!)}",
                    $"UseDuration = {FloatLiteral(json.GetProperty("use_duration"))}",
                    $"Range = {FloatLiteral(json.GetProperty("range"))}",
                    $"Description = {this.EmitValue(chatMessage, json.GetProperty("description"))}"
                };

                builder.AppendLine($"    public static global::Obsidian.API.Loot.InstrumentDefinition {MemberName(property.Name)} {{ get; }} = new() {{ {string.Join(", ", parts)} }};");
                builder.AppendLine();
            }

            builder.AppendLine("    /// <summary>");
            builder.AppendLine("    /// Every instrument in registry order, so an instrument's <c>Id</c> is its index.");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine($"    public static global::System.Collections.Generic.IReadOnlyList<global::Obsidian.API.Loot.InstrumentDefinition> All {{ get; }} = [{string.Join(", ", ordered.Select(property => MemberName(property.Name)))}];");
            builder.AppendLine("}");
            return builder.ToString();
        }

        public string EmitLootTables(List<(string Category, string Json)> categories)
        {
            var lootTable = this.compilation.GetTypeByMetadataName("Obsidian.API.Loot.LootTable")!;
            var builder = Header("Obsidian.API.Registries");
            builder.AppendLine("/// <summary>");
            builder.AppendLine("/// Vanilla loot tables, generated from <c>Assets/loot_table</c>. Each nested class holds one category (the first");
            builder.AppendLine("/// folder of the id), e.g. <c>minecraft:chests/simple_dungeon</c> is <c>LootTables.Chests.SimpleDungeon</c>.");
            builder.AppendLine("/// </summary>");
            builder.AppendLine("public static class LootTables");
            builder.AppendLine("{");

            var references = new List<(string Id, string Reference)>();
            foreach (var (category, json) in categories)
            {
                var className = MemberName(category);
                builder.AppendLine($"    public static class {className}");
                builder.AppendLine("    {");

                using var document = JsonDocument.Parse(json);
                foreach (var table in document.RootElement.EnumerateObject())
                {
                    this.currentOwner = table.Name;
                    var prefix = $"minecraft:{category}/";
                    var memberName = MemberName(table.Name.StartsWith(prefix, StringComparison.Ordinal) ? table.Name.Substring(prefix.Length) : table.Name);
                    var expression = this.EmitObject(lootTable, table.Value, null, $"Identifier = {Literal(table.Name)}");

                    builder.AppendLine($"        public static global::Obsidian.API.Loot.LootTable {memberName} {{ get; }} = {expression};");
                    builder.AppendLine();
                    references.Add((table.Name, $"{className}.{memberName}"));
                }

                builder.AppendLine("    }");
                builder.AppendLine();
            }

            builder.AppendLine("    /// <summary>");
            builder.AppendLine("    /// Every loot table, keyed by id.");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine("    public static FrozenDictionary<string, global::Obsidian.API.Loot.LootTable> All { get; } = new global::System.Collections.Generic.Dictionary<string, global::Obsidian.API.Loot.LootTable>()");
            builder.AppendLine("    {");
            foreach (var (id, reference) in references)
                builder.AppendLine($"        {{ {Literal(id)}, {reference} }},");

            builder.AppendLine("    }.ToFrozenDictionary();");
            builder.AppendLine("}");
            return builder.ToString();
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
                case SpecialType.System_Boolean:
                    return json.GetBoolean() ? "true" : "false";
                case SpecialType.System_Int32:
                    return json.GetInt32().ToString(CultureInfo.InvariantCulture);
                case SpecialType.System_Single:
                    return FloatLiteral(json);
            }

            var elementType = ElementType(target);
            if (elementType is not null)
                return json.ValueKind == JsonValueKind.Array
                    ? $"[{string.Join(", ", json.EnumerateArray().Select(value => this.EmitValue(elementType, value)))}]"
                    : json.ValueKind == JsonValueKind.String ? this.EmitString(target, json.GetString()!) : $"[{this.EmitValue(elementType, json)}]";

            if (target is INamedTypeSymbol { Name: "Dictionary", TypeArguments.Length: 2 } dictionary)
                return this.EmitDictionary(dictionary, json);

            if (json.ValueKind == JsonValueKind.String)
                return this.EmitString(target, json.GetString()!);

            var named = (INamedTypeSymbol)target;
            if (named.IsAbstract || named.TypeKind == TypeKind.Interface)
                return this.EmitPolymorphic(named, json);

            return this.EmitObject(named, json, null, null);
        }

        private string EmitPolymorphic(INamedTypeSymbol target, JsonElement json)
        {
            // Vanilla number providers: a plain number is a constant, an object without a type is uniform.
            if (SymbolEqualityComparer.Default.Equals(target, this.numberProvider) && json.ValueKind == JsonValueKind.Number)
                return $"new global::Obsidian.API.Loot.Numbers.ConstantNumber {{ Value = {FloatLiteral(json)} }}";

            var discriminator = IsAssignableTo(target, this.lootFunction) ? "function"
                : IsAssignableTo(target, this.lootCondition) ? "condition"
                : "type";

            var type = json.TryGetProperty(discriminator, out var value) ? value.GetString()!
                : SymbolEqualityComparer.Default.Equals(target, this.numberProvider) ? "minecraft:uniform"
                : string.Empty;

            var symbol = this.types.TryGetValue(type, out var candidates)
                ? candidates.FirstOrDefault(candidate => !candidate.IsAbstract && IsAssignableTo(candidate, target))
                : null;

            if (symbol is null)
            {
                this.Report(unknownType, type, target.Name);
                return "default!";
            }

            return this.EmitObject(symbol, json, discriminator, null);
        }

        /// <summary>
        /// Emits <c>new Type { Property = value, ... }</c>, mapping snake_case JSON keys to PascalCase properties.
        /// </summary>
        private string EmitObject(INamedTypeSymbol type, JsonElement json, string? discriminator, string? extraAssignment)
        {
            var assignments = new List<string>();
            if (extraAssignment is not null)
                assignments.Add(extraAssignment);

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

            var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return assignments.Count == 0 ? $"new {typeName}()" : $"new {typeName} {{ {string.Join(", ", assignments)} }}";
        }

        /// <summary>
        /// Emits a JSON string as <paramref name="target"/>: ids become registry references, and for collections a tag
        /// (<c>#minecraft:...</c>) expands to its entries while any other string becomes a one-element collection.
        /// </summary>
        private string EmitString(ITypeSymbol target, string value)
        {
            if (target is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
                target = nullable.TypeArguments[0];

            var elementType = ElementType(target);
            if (elementType is not null)
            {
                var entries = elementType.Name switch
                {
                    "EnchantmentDefinition" => this.ExpandReferences("enchantment", value),
                    "InstrumentDefinition" => this.ExpandReferences("instrument", value),
                    _ => [value]
                };

                return $"[{string.Join(", ", entries.Select(entry => this.EmitString(elementType, entry)))}]";
            }

            if (target.SpecialType == SpecialType.System_String)
                return Literal(value);

            if (target.TypeKind == TypeKind.Enum)
                return this.EmitEnum((INamedTypeSymbol)target, value);

            // Enums generated from enums.json (e.g. MobEffect) come from another generator and aren't in the compilation
            // generators see. Their members follow the same naming, and the name resolves from Obsidian.API.Registries.
            if (target.TypeKind == TypeKind.Error)
                return $"{target.Name}.{value.RemoveNamespace().ToPascalCase()}";

            switch (target.Name)
            {
                case "Item":
                    return this.EmitItem(value);
                case "EnchantmentDefinition":
                    return this.EmitRegistryReference("enchantment", "EnchantmentsRegistry", this.enchantments, value);
                case "InstrumentDefinition":
                    return this.EmitRegistryReference("instrument", "InstrumentsRegistry", this.instruments, value);
                case "ChatMessage":
                    return $"new global::Obsidian.API.ChatMessage {{ Text = {Literal(value)} }}";
            }

            this.Report(unknownProperty, target.Name, value, "string value");
            return "default!";
        }

        private string EmitDictionary(INamedTypeSymbol type, JsonElement json)
        {
            var entries = json.EnumerateObject().Select(property =>
                $"{{ {this.EmitString(type.TypeArguments[0], property.Name)}, {this.EmitValue(type.TypeArguments[1], property.Value)} }}");

            return $"new {type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {{ {string.Join(", ", entries)} }}";
        }

        /// <summary>
        /// The element type of an array or list type, or null for other types.
        /// </summary>
        private static ITypeSymbol? ElementType(ITypeSymbol type) => type switch
        {
            IArrayTypeSymbol array => array.ElementType,
            INamedTypeSymbol { Name: "IReadOnlyList" or "List", TypeArguments.Length: 1 } list => list.TypeArguments[0],
            _ => null
        };

        private string EmitEnum(INamedTypeSymbol type, string value)
        {
            var normalized = Normalize(value.Substring(value.IndexOf(':') + 1));
            var member = type.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(field => field.IsConst && Normalize(field.Name) == normalized);
            if (member is null)
                return this.Missing(type.Name, value, "default");

            return $"{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{member.Name}";
        }

        private string EmitItem(string id)
        {
            if (!this.items.TryGetProperty(id, out _))
                return this.Missing("item", id, "default");

            return $"{Registries}.ItemsRegistry.{id.RemoveNamespace().ToPascalCase()}";
        }

        private string EmitRegistryReference(string registry, string className, JsonElement entries, string id)
        {
            if (!entries.TryGetProperty(id, out _))
                return this.Missing(registry, id, "default!");

            return $"{Registries}.{className}.{MemberName(id)}";
        }

        /// <summary>
        /// Emits an item <c>HolderSet</c> (an id, a list of ids or an item tag) as an <c>ItemSet</c> of item ids.
        /// </summary>
        private string EmitItemSet(JsonElement json)
        {
            var ids = this.ExpandReferences("item", json).Select(id => this.items.TryGetProperty(id, out var item)
                ? item.GetProperty("protocol_id").GetInt32().ToString(CultureInfo.InvariantCulture)
                : this.Missing("item", id, "0"));

            return $"new global::Obsidian.API.Loot.ItemSet({string.Join(", ", ids)})";
        }

        /// <summary>
        /// Resolves a <c>HolderSet</c> of <paramref name="registry"/> to entry ids: a single id, a list of ids, or a
        /// <c>#tag</c> expanded like vanilla's tag loader (declaration order, nested tags in place, duplicates dropped).
        /// </summary>
        private List<string> ExpandReferences(string registry, JsonElement json)
        {
            var result = new List<string>();
            var values = json.ValueKind == JsonValueKind.Array ? json.EnumerateArray().Select(value => value.GetString()!) : [json.GetString()!];
            foreach (var value in values)
                this.Expand(registry, value, result, []);

            return result;
        }

        private List<string> ExpandReferences(string registry, string value)
        {
            var result = new List<string>();
            this.Expand(registry, value, result, []);
            return result;
        }

        private void Expand(string registry, string value, List<string> result, HashSet<string> resolving)
        {
            if (!value.StartsWith("#", StringComparison.Ordinal))
            {
                if (!result.Contains(value))
                    result.Add(value);

                return;
            }

            var key = registry + "/" + value.Substring(1).RemoveNamespace();
            if (!this.tags.TryGetProperty(key, out var tag) || !resolving.Add(key))
            {
                this.Report(unknownReference, "tag", value);
                return;
            }

            foreach (var entry in tag.GetProperty("values").EnumerateArray())
                this.Expand(registry, entry.GetString()!, result, resolving);

            resolving.Remove(key);
        }

        private static string EmitCost(JsonElement json) =>
            $"new({json.GetProperty("base").GetInt32()}, {json.GetProperty("per_level_above_first").GetInt32()})";

        private string Missing(string kind, string id, string fallback)
        {
            this.Report(unknownReference, kind, id);
            return fallback;
        }

        private void Report(DiagnosticDescriptor descriptor, params object[] arguments) =>
            this.context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, [.. arguments, this.currentOwner]));

        private static StringBuilder Header(string @namespace)
        {
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated/>");
            builder.AppendLine("using System.Collections.Frozen;");
            builder.AppendLine();
            builder.AppendLine($"namespace {@namespace};");
            builder.AppendLine();
            return builder;
        }

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
            for (var current = type; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, target))
                    return true;
            }

            return type.AllInterfaces.Any(@interface => SymbolEqualityComparer.Default.Equals(@interface, target));
        }

        private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, true);

        private static string FloatLiteral(JsonElement json) => json.GetSingle().ToString("R", CultureInfo.InvariantCulture) + "f";

        private static string Normalize(string value) => value.Replace("_", string.Empty).ToLowerInvariant();

        /// <summary>
        /// JSON key to property name: <c>bonus_rolls</c> → <c>BonusRolls</c>.
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
        /// Registry id or category to a member name: <c>minecraft:trial_chambers/reward_rare</c> → <c>TrialChambersRewardRare</c>.
        /// </summary>
        private static string MemberName(string id) => id.RemoveNamespace().ToPascalCase();
    }
}
