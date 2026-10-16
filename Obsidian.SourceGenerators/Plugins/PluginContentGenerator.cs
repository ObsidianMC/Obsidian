using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Plugins;

/// <summary>
/// Generates a plugin's blocks and tag keys from its <c>data/</c> folder, like Obsidian generates vanilla's from
/// vanilla's data: for each namespace, a <c>&lt;Namespace&gt;Blocks</c> class with the blocks, typed states and a
/// <c>Register</c> method the plugin calls from <c>ConfigureRegistry</c>, and a <c>&lt;Namespace&gt;Tags</c> class
/// with a key for each of its tags.
/// </summary>
/// <remarks>
/// A block is <c>data/&lt;namespace&gt;/blocks/&lt;path&gt;.json</c>, shaped like an entry of vanilla's blocks report:
/// <c>properties</c> maps each state property to its values (true and false make a bool property, two or more
/// consecutive numbers an int one, anything else an enum one in the listed order); <c>default</c> gives the default
/// state's values; and <c>hasCollision</c>, <c>opaque</c>, <c>hardness</c>, <c>explosionResistance</c>,
/// <c>requiresCorrectTool</c>, <c>lightEmission</c>, <c>behavior</c> (an <c>IBlockBehavior</c> type's full name),
/// <c>treeGrower</c> (a sapling's <c>tree</c>, <c>secondaryTree</c>, <c>megaTree</c>, <c>secondaryMegaTree</c>,
/// <c>flowers</c> and <c>secondaryFlowers</c> configured features and its <c>secondaryChance</c>) and <c>item</c>
/// (false for none, or <c>maxStackSize</c>, <c>maxDamage</c>, <c>enchantable</c> and <c>behavior</c>) set its
/// <c>BlockProperties</c>. Tags are data pack tag files, which the server loads itself; only their keys are
/// generated. A file that's invalid, or whose generated names are taken, is reported and left out.
/// </remarks>
[Generator]
public sealed class PluginContentGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor invalidBlock = new("OBSPL001", "Invalid block definition",
        "{0} isn't a valid block definition: {1}", "PluginContent", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor nameTaken = new("OBSPL002", "Generated name taken",
        "{0} would generate {1}, which is taken; rename the file or property", "PluginContent",
        DiagnosticSeverity.Error, true);

    // Members every generated state has, and Clone, which records can't have.
    private static readonly string[] stateMembers =
    [
        "From",
        "ToBlock",
        "Equals",
        "GetHashCode",
        "ToString",
        "Deconstruct",
        "EqualityContract",
        "PrintMembers",
        "Clone"
    ];

    // The metadata naming an additional file's path in the plugin's data folder.
    private const string DataPathMetadata = "build_metadata.AdditionalFiles.PluginDataPath";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Obsidian.MSBuild gives each file of the plugin's data folder its path in it, whatever the folder is called.
        var files = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (input, _) =>
            {
                var (file, options) = input;
                options.GlobalOptions.TryGetValue("build_property.projectdir", out var projectDirectory);

                var path = options.GetOptions(file).TryGetValue(DataPathMetadata, out var dataPath)
                    && !string.IsNullOrEmpty(dataPath)
                        ? Classify(dataPath.Replace('\\', '/'))
                        : DataPath(file.Path, projectDirectory);

                return (file, path);
            })
            .Where(static file => file.path is not null)
            .Select(static (file, cancellationToken) => new DataFile(
                file.file.Path,
                file.path!,
                file.path!.Contains("/blocks/") ? file.file.GetText(cancellationToken)?.ToString() : null))
            .Collect();

        var assemblyName = context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName);

        context.RegisterSourceOutput(files.Combine(assemblyName), static (output, source) =>
            Generate(output, source.Left, source.Right ?? "Plugin"));
    }

    private static void Generate(SourceProductionContext context, ImmutableArray<DataFile> files, string assembly)
    {
        // Obsidian's own projects have no plugin content.
        if (assembly is "Obsidian" or "Obsidian.API" || files.IsDefaultOrEmpty)
            return;

        var codeNamespace = string.Join(".", assembly.Split('.').Select(NamespacePart));
        var classNames = new HashSet<string>();

        foreach (var group in files.GroupBy(file => file.Path.Substring(0, file.Path.IndexOf('/'))))
        {
            var ordered = group.OrderBy(file => file.Path, StringComparer.Ordinal).ToList();
            var className = Identifier(group.Key);

            // Namespaces like my-mod and my.mod would make the same classes.
            if (!classNames.Add(className))
            {
                Report(context, nameTaken, ordered[0].FilePath, $"{className}Blocks and {className}Tags");
                continue;
            }

            var blocks = ReadBlocks(context, group.Key, ordered, $"{className}Blocks");
            var tags = ReadTags(context, ordered);

            if (blocks.Count > 0)
            {
                var source = WriteBlocks($"global::{codeNamespace}.{className}Blocks", codeNamespace, group.Key,
                    className, blocks);
                context.AddSource($"{className}Blocks.g.cs", source);
            }

            // Vanilla's tags have keys in TagsRegistry already.
            if (tags.Count > 0 && group.Key != "minecraft")
                context.AddSource($"{className}Tags.g.cs", WriteTags(codeNamespace, group.Key, className, tags));
        }
    }

    // The namespace's valid blocks whose generated names are free.
    private static List<BlockDefinition> ReadBlocks(
        SourceProductionContext context,
        string dataNamespace,
        List<DataFile> files,
        string className)
    {
        var blocks = new List<BlockDefinition>();
        var taken = new HashSet<string> { "Register", className };

        foreach (var file in files)
        {
            var parts = file.Path.Split('/');
            if (parts[1] != "blocks" || file.Json is not string json)
                continue;

            BlockDefinition block;
            try
            {
                block = BlockDefinition.Parse(dataNamespace, WithoutExtension(string.Join("/", parts.Skip(2))), json);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException
                or KeyNotFoundException)
            {
                Report(context, invalidBlock, file.FilePath, ex.Message);
                continue;
            }

            if (FindTakenName(block, taken) is string name)
            {
                Report(context, nameTaken, file.FilePath, name);
                continue;
            }

            taken.UnionWith(block.TypeLevelNames);
            blocks.Add(block);
        }

        return blocks;
    }

    // A name the block would generate that's taken already, in the blocks class or in its own state and enums.
    private static string? FindTakenName(BlockDefinition block, HashSet<string> taken)
    {
        var typeLevel = new HashSet<string>(taken);
        foreach (var name in block.TypeLevelNames)
        {
            if (!typeLevel.Add(name))
                return name;
        }

        var stateLevel = new HashSet<string>(stateMembers) { $"{block.Name}State" };
        foreach (var property in block.Properties)
        {
            if (!stateLevel.Add(Identifier(property.Name)))
                return $"{block.Name}State.{Identifier(property.Name)}";

            if (property.Kind != PropertyKind.Enum)
                continue;

            var members = new HashSet<string>();
            foreach (var value in property.Values)
            {
                if (!members.Add(Identifier(value)))
                    return $"{block.Name}{Identifier(property.Name)}.{Identifier(value)}";
            }
        }

        return null;
    }

    // The keys of the namespace's block and item tags whose generated names are free.
    private static List<(string Registry, string Path)> ReadTags(SourceProductionContext context, List<DataFile> files)
    {
        var tags = new List<(string Registry, string Path)>();
        var taken = new HashSet<(string Registry, string Name)>();

        foreach (var file in files)
        {
            var parts = file.Path.Split('/');
            if (parts[1] != "tags" || parts.Length < 4 || parts[2] is not ("block" or "item" or "entity_type"))
                continue;

            var path = WithoutExtension(string.Join("/", parts.Skip(3)));
            var name = Identifier(path);

            // A tag's key can't be named like the class it's in.
            if (name == $"{Identifier(parts[2])}s" || !taken.Add((parts[2], name)))
            {
                Report(context, nameTaken, file.FilePath, name);
                continue;
            }

            tags.Add((parts[2], path));
        }

        return tags;
    }

    // The generated code refers to everything by its full name, so that a block or property named like a type or
    // another member can't hide it.
    private static string WriteBlocks(
        string fullClassName,
        string codeNamespace,
        string dataNamespace,
        string className,
        List<BlockDefinition> blocks)
    {
        var builder = new CodeBuilder();

        builder.Line("// <auto-generated/>");
        builder.Line("#nullable enable");
        builder.Line();
        builder.Namespace(codeNamespace);
        builder.Line();
        builder.XmlSummary($"The blocks in data/{dataNamespace}/blocks.");
        builder.Type($"public static partial class {className}Blocks");

        foreach (var block in blocks)
        {
            builder.XmlSummary($"<c>{block.Identifier}</c>, once <see cref=\"Register\"/> registered it.");
            builder.Line($"public static global::Obsidian.API.Blocks.BlockType {block.Name} {{ get; private set; }} "
                + "= null!;");
            builder.Line();
        }

        builder.XmlSummary("Registers the blocks; call it from the plugin's <c>ConfigureRegistry</c>.");
        builder.Method("public static void Register(global::Obsidian.API.Plugins.IPluginRegistry registry)");

        foreach (var block in blocks)
            WriteRegistration(builder, fullClassName, block);

        builder.EndScope();

        foreach (var block in blocks.Where(block => block.Properties.Count > 0))
            WriteState(builder, fullClassName, block);

        builder.EndScope();

        return builder.ToString();
    }

    private static void WriteRegistration(CodeBuilder builder, string fullClassName, BlockDefinition block)
    {
        builder.Line($"{fullClassName}.{block.Name} = registry.RegisterBlock({Literal(block.Identifier)}, "
            + "new global::Obsidian.API.Blocks.BlockProperties");
        builder.Line("{");

        var lines = new List<string>();

        if (block.Properties.Count > 0)
        {
            const string type = "global::Obsidian.API.Blocks.StateProperty";
            var properties = block.Properties.Select(property => property.Kind switch
            {
                PropertyKind.Bool => $"{type}.Bool({Literal(property.Name)})",
                PropertyKind.Int => $"{type}.Int({Literal(property.Name)}, {property.Values[0]}, "
                    + $"{property.Values[property.Values.Count - 1]})",
                _ => $"{type}.Enum({Literal(property.Name)}, {string.Join(", ", property.Values.Select(Literal))})"
            });

            lines.Add($"    StateProperties = [{string.Join(", ", properties)}]");
        }

        if (block.DefaultState.Count > 0)
        {
            var values = block.DefaultState.Select(entry => $"[{Literal(entry.Key)}] = {Literal(entry.Value)}");
            lines.Add("    DefaultState = new global::System.Collections.Generic.Dictionary<string, string> "
                + $"{{ {string.Join(", ", values)} }}");
        }

        lines.AddRange(block.Settings.Select(setting => $"    {setting}"));

        if (block.Item is null)
            lines.Add("    Item = null");
        else if (block.Item.Count > 0)
            lines.Add("    Item = new global::Obsidian.API.Inventory.ItemProperties "
                + $"{{ {string.Join(", ", block.Item)} }}");

        for (var i = 0; i < lines.Count; i++)
            builder.Line(i < lines.Count - 1 ? $"{lines[i]}," : lines[i]);

        builder.Line("});");
    }

    // A record struct with the block's properties as typed values, to read a state from and turn into one.
    private static void WriteState(CodeBuilder builder, string fullClassName, BlockDefinition block)
    {
        const string culture = "global::System.Globalization.CultureInfo.InvariantCulture";
        var blockType = $"{fullClassName}.{block.Name}";

        foreach (var property in block.Properties.Where(property => property.Kind == PropertyKind.Enum))
        {
            var enumName = $"{block.Name}{Identifier(property.Name)}";

            builder.Line();
            builder.XmlSummary($"The values of <c>{block.Identifier}</c>'s <c>{property.Name}</c> property.");
            builder.Type($"public enum {enumName}");

            foreach (var value in property.Values)
                builder.Line($"{Identifier(value)},");

            builder.EndScope();
            builder.Line();
            builder.Line($"private static readonly string[] {enumName}Values = "
                + $"[{string.Join(", ", property.Values.Select(Literal))}];");
        }

        var parameters = block.Properties.Select(property =>
            $"{TypeOf(fullClassName, block, property)} {Identifier(property.Name)}");

        builder.Line();
        builder.XmlSummary($"A state of <c>{block.Identifier}</c>, by its properties' values.");
        builder.Type($"public readonly record struct {block.Name}State({string.Join(", ", parameters)})");

        builder.XmlSummary($"The values of a state of <c>{block.Identifier}</c>.");
        builder.Line($"public static {fullClassName}.{block.Name}State From(global::Obsidian.API.IBlock block) "
            + "=> new(");

        for (var i = 0; i < block.Properties.Count; i++)
        {
            var property = block.Properties[i];
            var type = TypeOf(fullClassName, block, property);
            var value = $"{blockType}.GetValue(block, {Literal(property.Name)})";
            var read = property.Kind switch
            {
                PropertyKind.Bool => $"{value} == \"true\"",
                PropertyKind.Int => $"int.Parse({value}, {culture})",
                _ => $"({type})global::System.Array.IndexOf({type}Values, {value})"
            };

            builder.Line($"    {read}{(i < block.Properties.Count - 1 ? "," : ");")}");
        }

        builder.Line();
        builder.XmlSummary("The block in this state.");
        builder.Line($"public global::Obsidian.API.IBlock ToBlock() => {blockType}.GetState("
            + "new global::System.Collections.Generic.Dictionary<string, string>");
        builder.Line("{");

        for (var i = 0; i < block.Properties.Count; i++)
        {
            var property = block.Properties[i];
            var member = $"this.{Identifier(property.Name)}";
            var write = property.Kind switch
            {
                PropertyKind.Bool => $"{member} ? \"true\" : \"false\"",
                PropertyKind.Int => $"{member}.ToString({culture})",
                _ => $"{TypeOf(fullClassName, block, property)}Values[(int){member}]"
            };

            builder.Line($"    [{Literal(property.Name)}] = {write}{(i < block.Properties.Count - 1 ? "," : "")}");
        }

        builder.Line("});");
        builder.EndScope();
    }

    private static string WriteTags(
        string codeNamespace,
        string dataNamespace,
        string className,
        List<(string Registry, string Path)> tags)
    {
        var builder = new CodeBuilder();

        builder.Line("// <auto-generated/>");
        builder.Line();
        builder.Namespace(codeNamespace);
        builder.Line();
        builder.XmlSummary($"Keys of the tags in data/{dataNamespace}/tags.");
        builder.Type($"public static partial class {className}Tags");

        foreach (var registry in tags.GroupBy(tag => tag.Registry))
        {
            builder.Type($"public static class {Identifier(registry.Key)}s");

            foreach (var (_, path) in registry)
            {
                var identifier = $"{dataNamespace}:{path}";

                builder.XmlSummary($"<c>{identifier}</c>.");
                builder.Line($"public static global::Obsidian.API.Registries.TagKey {Identifier(path)} {{ get; }} = "
                    + $"new({Literal(registry.Key)}, {Literal(identifier)});");
            }

            builder.EndScope();
        }

        builder.EndScope();

        return builder.ToString();
    }

    private static string TypeOf(string fullClassName, BlockDefinition block, PropertyDefinition property) =>
        property.Kind switch
        {
            PropertyKind.Bool => "bool",
            PropertyKind.Int => "int",
            _ => $"{fullClassName}.{block.Name}{Identifier(property.Name)}"
        };

    private static void Report(SourceProductionContext context, DiagnosticDescriptor descriptor, string file,
        string detail) => context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, file, detail));

    // A data file's path from its namespace on (ruby/blocks/lamp.json), for a file in the project's data folder, where
    // a project without Obsidian.MSBuild keeps it.
    private static string? DataPath(string path, string? projectDirectory)
    {
        if (string.IsNullOrEmpty(projectDirectory))
            return null;

        var normalized = path.Replace('\\', '/');
        var dataFolder = $"{projectDirectory!.Replace('\\', '/').TrimEnd('/')}/data/";

        return normalized.StartsWith(dataFolder, StringComparison.OrdinalIgnoreCase)
            ? Classify(normalized.Substring(dataFolder.Length))
            : null;
    }

    // The path if it's a block definition or a tag file.
    private static string? Classify(string dataPath)
    {
        var parts = dataPath.Split('/');

        return dataPath.EndsWith(".json", StringComparison.Ordinal)
            && parts.Length >= 3
            && parts[1] is "blocks" or "tags"
                ? dataPath
                : null;
    }

    // A C# identifier from an identifier's path or a value: the ASCII letters and digits of each word between other
    // characters, each word starting with a capital (red_blue and Red-Blue give RedBlue), starting with a letter.
    private static string Identifier(string value)
    {
        var name = new System.Text.StringBuilder();
        var wordStart = true;

        foreach (var c in value)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')))
            {
                wordStart = true;
                continue;
            }

            name.Append(wordStart ? char.ToUpperInvariant(c) : c);
            wordStart = false;
        }

        return name.Length == 0 || !char.IsLetter(name[0]) ? $"_{name}" : name.ToString();
    }

    // A part of a C# namespace from a part of an assembly name, which may hold characters C# names can't, or be a
    // keyword.
    private static string NamespacePart(string part)
    {
        var name = new string(part.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
        name = name.Length == 0 || char.IsDigit(name[0]) ? $"_{name}" : name;

        return SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : $"@{name}";
    }

    // Vanilla's identifier rules for a namespace and a path.
    private static bool IsIdentifierPart(string value, bool path) =>
        value.Length > 0 && value.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-' or '.'
            || (path && c == '/'));

    private static string WithoutExtension(string path) => path.Substring(0, path.Length - ".json".Length);

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    private sealed record DataFile(string FilePath, string Path, string? Json);

    private enum PropertyKind
    {
        Bool,
        Int,
        Enum
    }

    private sealed record PropertyDefinition(string Name, PropertyKind Kind, IReadOnlyList<string> Values);

    private sealed record BlockDefinition(
        string Identifier,
        string Name,
        IReadOnlyList<PropertyDefinition> Properties,
        IReadOnlyDictionary<string, string> DefaultState,
        IReadOnlyList<string> Settings,
        IReadOnlyList<string>? Item)
    {
        // The names the block takes in its blocks class.
        public IEnumerable<string> TypeLevelNames
        {
            get
            {
                yield return this.Name;

                if (this.Properties.Count == 0)
                    yield break;

                yield return $"{this.Name}State";

                foreach (var property in this.Properties.Where(property => property.Kind == PropertyKind.Enum))
                {
                    var enumName = $"{this.Name}{PluginContentGenerator.Identifier(property.Name)}";
                    yield return enumName;
                    yield return $"{enumName}Values";
                }
            }
        }

        public static BlockDefinition Parse(string dataNamespace, string path, string json)
        {
            if (!IsIdentifierPart(dataNamespace, path: false) || !IsIdentifierPart(path, path: true))
                throw new InvalidOperationException($"{dataNamespace}:{path} isn't a valid identifier.");

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var properties = new List<PropertyDefinition>();
            if (root.TryGetProperty("properties", out var propertiesElement))
            {
                foreach (var property in propertiesElement.EnumerateObject())
                {
                    var values = property.Value.EnumerateArray()
                        .Select(value => value.ValueKind == JsonValueKind.String
                            ? value.GetString()!
                            : throw new InvalidOperationException($"{property.Name}'s values must be strings."))
                        .ToList();

                    properties.Add(ParseProperty(property.Name, values));
                }
            }

            var defaultState = new Dictionary<string, string>();
            if (root.TryGetProperty("default", out var defaultElement))
            {
                foreach (var value in defaultElement.EnumerateObject())
                {
                    var property = properties.FirstOrDefault(property => property.Name == value.Name)
                        ?? throw new InvalidOperationException($"the default {value.Name} isn't a property.");

                    var text = value.Value.ValueKind == JsonValueKind.String
                        ? value.Value.GetString()!
                        : value.Value.GetRawText();

                    if (!property.Values.Contains(text))
                    {
                        throw new InvalidOperationException(
                            $"the default {value.Name} {text} isn't one of its values.");
                    }

                    defaultState[value.Name] = text;
                }
            }

            var settings = new List<string>();
            AddBool(root, "hasCollision", "HasCollision", settings);
            AddBool(root, "opaque", "Opaque", settings);
            AddFloat(root, "hardness", "Hardness", settings);
            AddFloat(root, "explosionResistance", "ExplosionResistance", settings);
            AddBool(root, "requiresCorrectTool", "RequiresCorrectTool", settings);
            AddInt(root, "lightEmission", "LightEmission", settings);

            if (root.TryGetProperty("lootTable", out var lootTable))
                settings.Add($"LootTable = {Literal(lootTable.GetString()!)}");

            AddBehavior(root, settings);
            AddTreeGrower(root, settings);

            List<string>? item = [];
            if (root.TryGetProperty("item", out var itemElement))
            {
                if (itemElement.ValueKind == JsonValueKind.False)
                {
                    item = null;
                }
                else if (itemElement.ValueKind == JsonValueKind.Object)
                {
                    AddInt(itemElement, "maxStackSize", "MaxStackSize", item);
                    AddInt(itemElement, "maxDamage", "MaxDamage", item);
                    AddInt(itemElement, "enchantable", "Enchantable", item);
                    AddBehavior(itemElement, item);
                }
                else if (itemElement.ValueKind != JsonValueKind.True)
                {
                    throw new InvalidOperationException("item must be true, false or the item's properties.");
                }
            }

            return new BlockDefinition(
                $"{dataNamespace}:{path}",
                PluginContentGenerator.Identifier(path),
                properties,
                defaultState,
                settings,
                item);
        }

        // Clients order a boolean property's values true first and an integer one's ascending, however they're listed.
        private static PropertyDefinition ParseProperty(string name, List<string> values)
        {
            if (name.Length == 0 || !name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
                throw new InvalidOperationException($"{name} isn't a valid property name.");

            if (values.Count == 0 || values.Distinct().Count() != values.Count)
                throw new InvalidOperationException($"{name} needs distinct values.");

            if (values.Count == 2 && values.Contains("true") && values.Contains("false"))
                return new PropertyDefinition(name, PropertyKind.Bool, ["true", "false"]);

            var numbers = values
                .Select(value => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                    ? n
                    : (int?)null)
                .ToList();

            // An int property has a range of at least two numbers.
            if (values.Count >= 2 && numbers.All(number => number is not null))
            {
                var sorted = numbers.Select(number => number!.Value).OrderBy(number => number).ToList();

                if (sorted.Zip(sorted.Skip(1), (low, high) => high - low).All(step => step == 1))
                {
                    var ordered = sorted.Select(number => number.ToString(CultureInfo.InvariantCulture)).ToList();
                    return new PropertyDefinition(name, PropertyKind.Int, ordered);
                }
            }

            return new PropertyDefinition(name, PropertyKind.Enum, values);
        }

        private static void AddBool(JsonElement element, string key, string member, List<string> settings)
        {
            if (element.TryGetProperty(key, out var value))
                settings.Add($"{member} = {(value.GetBoolean() ? "true" : "false")}");
        }

        private static void AddInt(JsonElement element, string key, string member, List<string> settings)
        {
            if (element.TryGetProperty(key, out var value))
                settings.Add($"{member} = {value.GetInt32().ToString(CultureInfo.InvariantCulture)}");
        }

        private static void AddFloat(JsonElement element, string key, string member, List<string> settings)
        {
            if (!element.TryGetProperty(key, out var value))
                return;

            var number = value.GetSingle();
            if (float.IsNaN(number) || float.IsInfinity(number))
                throw new InvalidOperationException($"{key} {value.GetRawText()} is too large.");

            settings.Add($"{member} = {number.ToString("R", CultureInfo.InvariantCulture)}f");
        }

        // A sapling's trees: TreeGrower's members, the features as identifiers.
        private static void AddTreeGrower(JsonElement element, List<string> settings)
        {
            if (!element.TryGetProperty("treeGrower", out var value))
                return;

            if (value.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("treeGrower must be an object.");

            var members = new List<string>();
            AddFeature(value, "tree", "Tree", members);
            AddFeature(value, "secondaryTree", "SecondaryTree", members);
            AddFeature(value, "megaTree", "MegaTree", members);
            AddFeature(value, "secondaryMegaTree", "SecondaryMegaTree", members);
            AddFeature(value, "flowers", "Flowers", members);
            AddFeature(value, "secondaryFlowers", "SecondaryFlowers", members);
            AddFloat(value, "secondaryChance", "SecondaryChance", members);

            settings.Add($"TreeGrower = new global::Obsidian.API.Blocks.TreeGrower {{ {string.Join(", ", members)} }}");
        }

        private static void AddFeature(JsonElement element, string key, string member, List<string> settings)
        {
            if (!element.TryGetProperty(key, out var value))
                return;

            if (value.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException($"{key} must be a configured feature's identifier.");

            settings.Add($"{member} = {SymbolDisplay.FormatLiteral(value.GetString()!, quote: true)}");
        }

        private static void AddBehavior(JsonElement element, List<string> settings)
        {
            if (!element.TryGetProperty("behavior", out var value))
                return;

            // A type's full name, with any part that's a keyword escaped, as it would be in C#.
            var type = value.GetString() ?? "";
            var parts = type.Split('.');

            if (parts.Any(part => part.Length == 0
                || !(char.IsLetter(part[0]) || part[0] == '_')
                || !part.All(c => char.IsLetterOrDigit(c) || c == '_')))
            {
                throw new InvalidOperationException($"{type} isn't a type name.");
            }

            var escaped = parts.Select(part => SyntaxFacts.GetKeywordKind(part) == SyntaxKind.None ? part : $"@{part}");
            settings.Add($"Behavior = new global::{string.Join(".", escaped)}()");
        }
    }
}
