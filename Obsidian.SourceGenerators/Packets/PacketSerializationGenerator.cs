using System.Collections.Immutable;
using System.IO;
using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Packets;

/// <summary>
/// Generates packets' properties and their <c>Serialize</c>/<c>Populate</c> methods, and the vanilla records and enums
/// they write, from how vanilla writes them (<c>packets.json</c> and <c>packet_types.json</c>, see
/// <see cref="Protocol"/> and <see cref="Encodings"/>).
/// </summary>
/// <remarks>
/// <para>
/// In Obsidian.API it emits the vanilla types into <see cref="Protocol.TypesNamespace"/>, except those whose name a
/// hand-written type already has. In Obsidian it emits each packet whose methods aren't hand-written: a packet with a
/// hand-written <c>Serialize</c> (clientbound) or <c>Populate</c> (serverbound) is left alone.
/// </para>
/// <para>
/// Packets vanilla writes in a way this can't reproduce (fields written only sometimes, in loops or packed together,
/// or with codecs the asset generator can't describe) get warning OBSPK001 and need their methods written by hand.
/// Generators can't see each other's output, so this writes the methods itself rather than leaving them to another
/// generator.
/// </para>
/// </remarks>
[Generator]
public sealed class PacketSerializationGenerator : IIncrementalGenerator
{
    private const string GeneratedCode = "[global::System.CodeDom.Compiler.GeneratedCode(\"Obsidian.SourceGenerators\", \"1.0.0\")]";

    private static readonly DiagnosticDescriptor notGenerated = new(
        "OBSPK001",
        "Packet isn't generated",
        "{0} isn't generated: {1}",
        "Packets",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The packet has no hand-written Serialize/Populate method, and vanilla writes it in a way the generator can't reproduce. Write its properties and methods by hand.");

    // Members of the packet base classes, which generated properties can't be named.
    private static readonly HashSet<string> reservedNames = ["Id", "Serialize", "Populate", "HandleAsync", "Deserialize"];

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var files = context.AdditionalTextsProvider
            .Where(file => Path.GetFileName(file.Path) is "packets.json" or "packet_types.json" or "blocks.json")
            .Select((file, token) => (name: Path.GetFileNameWithoutExtension(file.Path), json: file.GetText(token)!.ToString()))
            .Collect();

        context.RegisterSourceOutput(context.CompilationProvider.Combine(files), Generate);
    }

    private static void Generate(SourceProductionContext context, (Compilation compilation, ImmutableArray<(string name, string json)> files) input)
    {
        var (compilation, files) = input;
        if (compilation.AssemblyName is not ("Obsidian.API" or "Obsidian"))
            return;

        // Obsidian.API only has the types; Obsidian has both.
        var packetsJson = files.GetJsonFromArray("packets");
        var typesJson = files.GetJsonFromArray("packet_types");
        if (typesJson is null)
            return;

        if (compilation.AssemblyName == "Obsidian.API")
            GenerateTypes(context, compilation, Protocol.Parse("[]", typesJson), files.GetJsonFromArray("blocks"));
        else if (packetsJson is not null)
            GeneratePackets(context, compilation, Protocol.Parse(packetsJson, typesJson));
    }

    private static void GenerateTypes(SourceProductionContext context, Compilation compilation, Protocol protocol, string? blocksJson)
    {
        // Names Obsidian.API already has: hand-written types, and the block state enums BlocksGenerator emits (which
        // this can't see). An enum with vanilla's members and values is used as is; any other type takes the name.
        var blockEnums = blocksJson is null ? [] : BlockProperty.EnumsOf(blocksJson);
        string? Existing(ProtocolType type)
        {
            var symbol = compilation.GetSymbolsWithName(type.Name, SymbolFilter.Type).OfType<INamedTypeSymbol>().FirstOrDefault();
            if (symbol is not null)
                return MatchesEnum(symbol, type) ? null : $"a hand-written type in Obsidian.API is already named {type.Name}";
            if (blockEnums.TryGetValue(type.Name, out var members))
            {
                var matches = type.IsEnum && members.Length == type.Values.Count
                    && type.Values
                        .Select((value, index) => Protocol.PascalCase(value.Name) == members[index] && value.Value == index)
                        .All(match => match);
                return matches ? null : $"a block state enum is already named {type.Name}";
            }
            return null;
        }

        var encodings = new Encodings(protocol, Existing);
        foreach (var type in protocol.Types.Values)
        {
            // Skip types that can't be generated, and enums that already exist with the same members.
            if (encodings.Problem(type) is not null || blockEnums.ContainsKey(type.Name)
                || compilation.GetSymbolsWithName(type.Name, SymbolFilter.Type).Any())
                continue;

            var code = new CodeBuilder()
                .Line("// <auto-generated/>")
                .Line("#nullable enable")
                .Namespace(Protocol.TypesNamespace)
                .Line()
                .Line($"/// <summary>Vanilla's <c>{type.MojangName}</c>.</summary>")
                .Line(GeneratedCode);
            if (type.IsEnum)
                AppendEnum(code, type);
            else
                AppendRecord(code, type, encodings);

            context.AddSource($"Types.{type.Name}.g.cs", code.ToString());
        }
    }

    private static void AppendEnum(CodeBuilder code, ProtocolType type)
    {
        code.Type($"public enum {type.Name}");
        foreach (var (name, value) in type.Values)
            code.Line($"{Protocol.PascalCase(name)} = {value},");
        code.EndScope();
    }

    private static void AppendRecord(CodeBuilder code, ProtocolType type, Encodings encodings)
    {
        var fields = type.Fields.Where(field => field.Sent == true).ToList();
        var names = fields.ToDictionary(field => field, field => PropertyName(field, type.Name, isPacket: false));

        code.Type($"public sealed record class {type.Name} : global::Obsidian.API.INetworkSerializable<{type.Name}>");
        foreach (var field in fields)
            code.Line($"public required {encodings.CSharpType(field)} {names[field]} {{ get; init; }}");

        code.Line();
        code.Method($"public static void Write({type.Name} value, global::Obsidian.API.INetStreamWriter writer)");
        foreach (var field in fields)
            encodings.Write(code, field, $"value.{names[field]}");
        code.EndScope();

        code.Line();
        code.Method($"public static {type.Name} Read(global::Obsidian.API.INetStreamReader reader)");
        var values = new List<string>();
        foreach (var field in fields)
        {
            var value = $"{names[field].ToLowerInvariant()}Value";
            code.Line($"var {value} = {encodings.Read(code, field)};");
            values.Add($"{names[field]} = {value}");
        }
        code.Line($"return new {type.Name} {{ {string.Join(", ", values)} }};");
        code.EndScope();
        code.EndScope();
    }

    private static void GeneratePackets(SourceProductionContext context, Compilation compilation, Protocol protocol)
    {
        // Obsidian.API emitted the vanilla types that carry the generated-code attribute; the rest have names a
        // hand-written type already uses there.
        var encodings = new Encodings(protocol, type =>
            compilation.GetTypeByMetadataName($"{Protocol.TypesNamespace}.{type.Name}") is { } symbol
            && (symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.Name == "GeneratedCodeAttribute") || MatchesEnum(symbol, type))
                ? null
                : $"another type in Obsidian.API is already named {type.Name}");

        var commonDone = new HashSet<string>();
        foreach (var packet in protocol.Packets)
        {
            var isCommon = PacketClassesGenerator.commonPacketNames.Contains(packet.ResourceId);
            if (isCommon && !commonDone.Add(packet.ResourceId))
                continue;

            var @namespace = isCommon ? "Obsidian.Net.Packets.Common" : $"Obsidian.Net.Packets.{packet.State}.{packet.Namespace}";
            var className = $"{packet.Name}Packet";
            var symbol = compilation.GetTypeByMetadataName($"{@namespace}.{className}");
            var isClientbound = packet.Namespace == "Clientbound";
            var handWritten = isCommon
                ? Declares(symbol, "Serialize") || Declares(symbol, "Populate")
                : Declares(symbol, isClientbound ? "Serialize" : "Populate");

            // Packets the asset generator couldn't read (bundles) have no "sent", and empty ones need nothing.
            var fields = packet.Fields.Where(field => field.Sent == true).ToList();
            if (handWritten || packet.Fields.Any(field => field.Sent is null) || fields.Count == 0)
                continue;

            var problem = fields.Select(encodings.Problem).FirstOrDefault(fieldProblem => fieldProblem is not null);
            if (problem is not null)
            {
                var description = isCommon ? $"Common {className}" : $"{packet.State} {packet.Namespace.ToLowerInvariant()} {className}";
                context.ReportDiagnostic(Diagnostic.Create(notGenerated, symbol?.Locations.FirstOrDefault() ?? Location.None, description, problem));
                continue;
            }

            context.AddSource($"Serialization.{(isCommon ? "Common" : packet.State + "." + packet.Namespace)}.{className}.g.cs",
                PacketSource(packet, @namespace, className, isCommon, isClientbound, fields, encodings));
        }
    }

    private static string PacketSource(ProtocolPacket packet, string @namespace, string className, bool isCommon, bool isClientbound,
        List<ProtocolField> fields, Encodings encodings)
    {
        var names = fields.ToDictionary(field => field, field => PropertyName(field, className, isPacket: true));
        var code = new CodeBuilder()
            .Line("// <auto-generated/>")
            .Line("#nullable enable")
            .Namespace(@namespace)
            .Line()
            .Type(isCommon ? $"public partial record class {className}" : $"public partial class {className}");

        // Clientbound packets are built by the server, so their fields are required; read packets fill theirs in.
        foreach (var field in fields)
        {
            var type = encodings.CSharpType(field);
            code.Line($"/// <summary>Vanilla's <c>{field.Name}</c>.</summary>");
            code.Line(isCommon ? $"public {type} {names[field]} {{ get; set; }} = default!;"
                : isClientbound ? $"public required {type} {names[field]} {{ get; init; }}"
                : $"public {type} {names[field]} {{ get; private set; }} = default!;");
        }

        if (isCommon || isClientbound)
        {
            code.Line();
            code.Method("public override void Serialize(global::Obsidian.API.INetStreamWriter writer)");
            foreach (var field in fields)
                encodings.Write(code, field, $"this.{names[field]}");
            code.EndScope();
        }

        if (isCommon || !isClientbound)
        {
            code.Line();
            code.Method("public override void Populate(global::Obsidian.API.INetStreamReader reader)");
            foreach (var field in fields)
            {
                var value = encodings.Read(code, field);
                code.Line($"this.{names[field]} = {value};");
            }
            code.EndScope();
        }

        return code.EndScope().ToString();
    }

    /// <summary>
    /// A field's property name: vanilla's name in PascalCase, with <c>Value</c> after names the containing type or, in
    /// packets, the packet base classes already use (a packet's <c>Id</c> is its packet id).
    /// </summary>
    private static string PropertyName(ProtocolField field, string containingType, bool isPacket)
    {
        var name = Protocol.PascalCase(field.Name);
        return name == containingType || isPacket && reservedNames.Contains(name) ? name + "Value" : name;
    }

    /// <summary>Whether an existing enum has exactly a vanilla enum's members (in PascalCase) and values.</summary>
    private static bool MatchesEnum(INamedTypeSymbol symbol, ProtocolType type)
    {
        if (!type.IsEnum || symbol.TypeKind != TypeKind.Enum)
            return false;

        var members = symbol.GetMembers().OfType<IFieldSymbol>().Where(field => field.HasConstantValue)
            .ToDictionary(field => field.Name, field => Convert.ToInt32(field.ConstantValue));
        return members.Count == type.Values.Count
            && type.Values.All(value => members.TryGetValue(Protocol.PascalCase(value.Name), out var existing) && existing == value.Value);
    }

    /// <summary>Whether hand-written code (not generated) declares a member of the type.</summary>
    private static bool Declares(INamedTypeSymbol? type, string member) =>
        type?.GetMembers(member).Any(symbol => !symbol.DeclaringSyntaxReferences.IsEmpty) == true;
}
