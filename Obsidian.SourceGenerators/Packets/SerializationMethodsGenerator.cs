//using Obsidian.SourceGenerators.Packets.Attributes;
//using System.Collections.Immutable;
//using System.Text;
//using System.Threading;

//namespace Obsidian.SourceGenerators.Packets;

//[Generator]
//public partial class SerializationMethodsGenerator : IIncrementalGenerator
//{
//    //private static Property varInt = null!; // Used for default collection length prefix

//    //public void Initialize(IncrementalGeneratorInitializationContext context)
//    //{
//    //    var packetProperties = context.SyntaxProvider.CreateSyntaxProvider(
//    //        predicate: this.ProviderPredicate,
//    //        transform: this.TranformProperties);

//    //    varInt = new Property("VarInt", "int", AttributeFlags.Field | AttributeFlags.VarLength, [new VarLengthBehavior(null!)]);

//    //    var compilation = context.CompilationProvider.Combine(packetProperties.Collect());

//    //    context.RegisterSourceOutput(compilation, Generate);
//    //}

//    //private bool ProviderPredicate(SyntaxNode node, CancellationToken token) =>
//    //    (node is FieldDeclarationSyntax field && HasFieldAttribute(field.AttributeLists)) ||
//    //               (node is PropertyDeclarationSyntax property && HasFieldAttribute(property.AttributeLists));

//    //private SyntaxNode TranformProperties(GeneratorSyntaxContext context, CancellationToken token)
//    //{
//    //    return context.Node;
//    //}

//    //internal void Generate(SourceProductionContext context, (Compilation compilation, ImmutableArray<SyntaxNode> packetProperties) output)
//    //{
//    //    this.Execute(context, output.compilation, output.packetProperties);
//    //}

//    //internal void Execute(SourceProductionContext context, Compilation compilation, ImmutableArray<SyntaxNode> packetProperties)
//    //{
//    //    if (compilation.AssemblyName != "Obsidian")
//    //        return;

//    //    try
//    //    {
//    //        DangerousExecute(context, compilation, packetProperties);
//    //    }
//    //    catch (Exception e)
//    //    {
//    //        DiagnosticHelper.ReportDiagnostic(context, DiagnosticSeverity.Error, $"Source generation error: {e.Message} {e.StackTrace}");
//    //    }
//    //}

//    //private void DangerousExecute(SourceProductionContext context, Compilation compilation, ImmutableArray<SyntaxNode> packetProperties)
//    //{
//    //    // Get all packet fields
//    //    var properties = new List<Property>();
//    //    foreach (var member in packetProperties)
//    //    {
//    //        SemanticModel model = compilation.GetSemanticModel(member.SyntaxTree);
//    //        if (member is FieldDeclarationSyntax field)
//    //        {
//    //            foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
//    //            {
//    //                if (model.GetDeclaredSymbol(variable) is ISymbol symbol)
//    //                {
//    //                    properties.Add(new Property(field, symbol));
//    //                }
//    //            }
//    //        }
//    //        else if (member is PropertyDeclarationSyntax property)
//    //        {
//    //            if (model.GetDeclaredSymbol(member) is ISymbol symbol)
//    //            {
//    //                properties.Add(new Property(property, symbol));
//    //            }
//    //        }
//    //    }

//    //    // Generate partial classes
//    //    var typeToProperties = properties
//    //        .GroupBy(static field => field.ContainingType, SymbolEqualityComparer.Default);
//    //    foreach ((ISymbol? symbol, List<Property> fields) in typeToProperties)
//    //    {
//    //        if (symbol is not INamedTypeSymbol @class)
//    //        {
//    //            continue;
//    //        }

//    //        if (@class.IsStatic || @class.DeclaredAccessibility != Accessibility.Public)
//    //        {
//    //            context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.ContainingTypeNotViable, @class.Locations.First(), @class.Name));
//    //            continue;
//    //        }

//    //        string classSource = ProcessClass(@class, fields);
//    //        context.AddSource($"{@class.Name}.Serialization.cs", SourceText.From(classSource, Encoding.UTF8));
//    //    }
//    //}

//    //private string ProcessClass(INamedTypeSymbol classSymbol, List<Property> fields)
//    //{
//    //    fields.Sort((a, b) => a.Order.CompareTo(b.Order));

//    //    string @namespace = classSymbol.ContainingNamespace.ToDisplayString();
//    //    string className = classSymbol.IsGenericType
//    //        ? $"{classSymbol.Name}<{string.Join(", ", classSymbol.TypeParameters.Select(parameter => parameter.Name))}>"
//    //        : classSymbol.Name;

//    //    var interfaces = classSymbol.AllInterfaces;
//    //    bool clientbound = interfaces.Any(@interface => @interface.Name == Vocabulary.ClientboundInterface);
//    //    bool serverbound = interfaces.Any(@interface => @interface.Name == Vocabulary.ServerboundInterface);

//    //    var methods = classSymbol.GetMembers().OfType<IMethodSymbol>();
//    //    var source = new CodeBuilder();

//    //    var usings = new HashSet<string>();
//    //    foreach (SyntaxReference declaration in classSymbol.DeclaringSyntaxReferences)
//    //    {
//    //        SyntaxNode root = declaration.GetSyntax().GetRoot();
//    //        foreach (SyntaxNode child in root.DescendantNodes())
//    //        {
//    //            if (child is UsingDirectiveSyntax { Name: NameSyntax name })
//    //            {
//    //                usings.Add(name.ToString());
//    //            }
//    //        }
//    //    }

//    //    usings.Add("Obsidian.Net");
//    //    usings.Add("Obsidian.Utilities");
//    //    usings.Add("System.Runtime.CompilerServices");

//    //    foreach (string @using in usings.OrderBy(s => s))
//    //    {
//    //        source.Using(@using);
//    //    }
//    //    source.Line();

//    //    source.Namespace(@namespace);
//    //    source.Line();

//    //    source.Type(classSymbol);

//    //    var bodySource = CodeBuilder.WithIndentationOf(source.Indentation + 1);

//    //    // Serialize(MinecraftStream stream)
//    //    bool createSerializationMethod =
//    //        clientbound
//    //        && !methods.Any(m => m.Name == "Serialize" && m.Parameters.Length == 1 && m.Parameters[0].Type.Name == "MinecraftStream")
//    //        && TryCreateSerializationMethod(bodySource, fields);
//    //    if (createSerializationMethod)
//    //    {
//    //        source.XmlSummary("Serializes data from this packet into <see cref=\"MinecraftStream\"/>.\n<b>AUTOGENERATED</b>");
//    //        source.XmlParam("stream", "Target stream that this packet's data is written to.");

//    //        source.Method("public void Serialize(MinecraftStream stream)");
//    //        source.Append(bodySource);
//    //        source.EndScope();
//    //    }

//    //    bodySource = CodeBuilder.WithIndentationOf(source.Indentation + 1);

//    //    if (serverbound)
//    //    {
//    //        if (createSerializationMethod)
//    //            source.Line();

//    //        // Deserialize(byte[] data)
//    //        if (!methods.Any(m => m.Name == "Deserialize" && m.Parameters.Length == 1 && m.Parameters[0].Type.ToDisplayString() == "byte[]"))
//    //        {
//    //            source.XmlSummary($"Deserializes byte data into <see cref=\"{classSymbol.Name}\"/> packet.\n<b>AUTOGENERATED</b>");
//    //            source.XmlParam("data", "Data used to populate the packet.");
//    //            source.XmlReturns("Deserialized packet.");

//    //            source.Method($"public static {className} Deserialize(byte[] data)");
//    //            source.Line("using var stream = new MinecraftStream(data);");
//    //            source.Line("return Deserialize(stream);");
//    //            source.EndScope();
//    //        }

//    //        // Deserialize(MinecraftStream stream)
//    //        if (!methods.Any(m => m.Name == "Deserialize" && m.Parameters.Length == 1 && m.Parameters[0].Type.Name == "MinecraftStream"))
//    //        {
//    //            source.Line();
//    //            source.XmlSummary($"Deserializes data from a <see cref=\"MinecraftStream\"/> into <see cref=\"{classSymbol.Name}\"/> packet.\n<b>AUTOGENERATED</b>");
//    //            source.XmlParam("stream", "Stream that is read from to populate the packet.");
//    //            source.XmlReturns("Deserialized packet.");

//    //            source.Method($"public static {className} Deserialize(MinecraftStream stream)");
//    //            source.Line($"var packet = new {className}();");
//    //            source.Line("packet.Populate(stream);");
//    //            source.Line("return packet;");
//    //            source.EndScope();
//    //        }
//    //    }

//    //    bodySource = CodeBuilder.WithIndentationOf(source.Indentation + 1);

//    //    if (serverbound && TryCreatePopulateMethod(bodySource, fields, syntaxProvider))
//    //    {
//    //        // Populate(byte[] data)
//    //        if (!methods.Any(m => m.Name == "Populate" && m.Parameters.Length == 1 && m.Parameters[0].Type.ToDisplayString() == "byte[]"))
//    //        {
//    //            source.Line();
//    //            source.XmlSummary($"Populates this packet with data from a <see cref=\"byte\"/>[] buffer.");
//    //            source.XmlParam("data", "Data used to populate this packet.");
//    //            source.Method("public void Populate(byte[] data)");
//    //            source.Line("using var stream = new MinecraftStream(data);");
//    //            source.Line("Populate(stream);");
//    //            source.EndScope();
//    //        }

//    //        // Populate(MinecraftStream stream)
//    //        if (!methods.Any(m => m.Name == "Populate" && m.Parameters.Length == 1 && m.Parameters[0].Type.Name == "MinecraftStream"))
//    //        {
//    //            source.Line();
//    //            source.XmlSummary("Populates this packet with data from a <see cref=\"MinecraftStream\"/>.");
//    //            source.XmlParam("stream", "Stream used to populate this packet.");

//    //            source.Method("public void Populate(MinecraftStream stream)");
//    //            source.Append(bodySource);
//    //            source.EndScope();
//    //        }
//    //    }

//    //    bodySource.Clear();

//    //    source.EndScope(); // End of type

//    //    return source.ToString();
//    //}

//    //private bool TryCreateSerializationMethod(CodeBuilder builder, List<Property> properties)
//    //{
//    //    string streamName = "packetStream";

//    //    builder.Line($"using var {streamName} = new MinecraftStream();");
//    //    foreach (Property property in properties)
//    //    {
//    //        if (property.IsCollection)
//    //        {
//    //            if (!TrySerializePropertyCollection(streamName, property, properties, builder))
//    //                return false;
//    //        }
//    //        else
//    //        {
//    //            if (!TrySerializeProperty(streamName, property, properties, builder))
//    //                return false;
//    //        }
//    //    }

//    //    builder.Line();
//    //    builder.Line("stream.Lock.Wait();");
//    //    builder.Line($"stream.WriteVarInt(Id.GetVarIntLength() + (int){streamName}.Length);");
//    //    builder.Line("stream.WriteVarInt(Id);");
//    //    builder.Line($"{streamName}.Position = 0;");
//    //    builder.Line($"{streamName}.CopyTo(stream);");
//    //    builder.Line("stream.Lock.Release();");
//    //    return true;
//    //}

//    //private static bool HasFieldAttribute(SyntaxList<AttributeListSyntax> attributeLists) =>
//    //     attributeLists.SelectMany(list => list.Attributes).Any(attribute => Vocabulary.AttributeNamesEqual(attribute.Name.ToString(), Vocabulary.FieldAttribute));
//    public void Initialize(IncrementalGeneratorInitializationContext context) { }
//}
