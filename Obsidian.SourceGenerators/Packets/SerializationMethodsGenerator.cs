using Obsidian.SourceGenerators.Packets.Attributes;
using System.Text;

namespace Obsidian.SourceGenerators.Packets;

[Generator]
public partial class SerializationMethodsGenerator : ISourceGenerator
{
    private static Property varInt; // Used for default collection length prefix

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new SyntaxProvider());

        varInt = new Property
        {
            Type = "int",
            Attributes =
            [
                    new VarLengthBehavior(null)
            ],
            Flags = AttributeFlags.Field | AttributeFlags.VarLength
        };
    }

    public void Execute(GeneratorExecutionContext context)
    {
        if (context.Compilation.AssemblyName != "Obsidian")
            return;

        try
        {
            DangerousExecute(context);
        }
        catch (Exception e)
        {
            DiagnosticHelper.ReportDiagnostic(context, DiagnosticSeverity.Error, $"Source generation error: {e.Message} {e.StackTrace}");
        }
    }

    private void DangerousExecute(GeneratorExecutionContext context)
    {
        if (context.SyntaxReceiver is not SyntaxProvider syntaxProvider)
            return;

        Compilation compilation = context.Compilation;

        // Get all packet fields
        var properties = new List<Property>();
        foreach (MemberDeclarationSyntax member in syntaxProvider.WithContext(context).GetSyntaxNodes())
        {
            SemanticModel model = compilation.GetSemanticModel(member.SyntaxTree);
            if (member is FieldDeclarationSyntax field)
            {
                foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
                {
                    ISymbol symbol = model.GetDeclaredSymbol(variable);
                    properties.Add(new Property(field, symbol));
                }
            }
            else if (member is PropertyDeclarationSyntax property)
            {
                ISymbol symbol = model.GetDeclaredSymbol(member);
                properties.Add(new Property(property, symbol));
            }
        }

        // Generate partial classes
        foreach (var group in properties.GroupBy(field => field.ContainingType))
        {
            var @class = group.Key;

            if (@class.IsStatic || @class.DeclaredAccessibility != Accessibility.Public)
            {
                context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.ContainingTypeNotViable, @class.Locations.First(), @class.Name));
                continue;
            }

            var fields = group.ToList();
            string classSource = ProcessClass(@class, fields, syntaxProvider);
            context.AddSource($"{@class.Name}.Serialization.cs", SourceText.From(classSource, Encoding.UTF8));
        }
    }

    private string ProcessClass(INamedTypeSymbol classSymbol, List<Property> fields, SyntaxProvider syntaxProvider)
    {
        fields.Sort((a, b) => a.Order.CompareTo(b.Order));

        string @namespace = classSymbol.ContainingNamespace.ToDisplayString();
        string className = classSymbol.IsGenericType ? $"{classSymbol.Name}<{string.Join(", ", classSymbol.TypeParameters.Select(parameter => parameter.Name))}>" : classSymbol.Name;

        var interfaces = classSymbol.AllInterfaces;
        bool clientbound = interfaces.Any(@interface => @interface.Name == Vocabulary.ClientboundInterface);
        bool serverbound = interfaces.Any(@interface => @interface.Name == Vocabulary.ServerboundInterface);

        var methods = classSymbol.GetMembers().OfType<IMethodSymbol>();

        var source = new CodeBuilder();

        var usings = new HashSet<string>();

        foreach (SyntaxReference declaration in classSymbol.DeclaringSyntaxReferences)
        {
            SyntaxNode root = declaration.GetSyntax().GetRoot();
            foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
            {
                usings.Add(usingDirective.Name.ToString());
            }
        }

        usings.Add("Obsidian.Net");
        usings.Add("Obsidian.Utilities");
        usings.Add("System.Runtime.CompilerServices");

        foreach (string @using in usings.OrderBy(s => s))
        {
            source.Using(@using);
        }
        source.Line();

        source.Namespace(@namespace);
        source.Line();

        source.Type(classSymbol);

        var bodySource = CodeBuilder.WithIndentationOf(source.Indentation + 1);

        // Serialize(MinecraftStream stream)
        bool createSerializationMethod =
            clientbound
            && !methods.Any(m => m.Name == "Serialize" && m.Parameters.Length == 1 && m.Parameters[0].Type.Name == "MinecraftStream")
            && TryCreateSerializationMethod(bodySource, fields, syntaxProvider);
        if (createSerializationMethod)
        {
            source.XmlSummary("Serializes data from this packet into <see cref=\"MinecraftStream\"/>.\n<b>AUTOGENERATED</b>");
            source.XmlParam("stream", "Target stream that this packet's data is written to.");

            source.Method("public void Serialize(MinecraftStream stream)");
            source.Append(bodySource);
            source.EndScope();
        }

        bodySource = CodeBuilder.WithIndentationOf(source.Indentation + 1);

        if (serverbound)
        {
            if (createSerializationMethod)
                source.Line();

            // Deserialize(byte[] data)
            if (!methods.Any(m => m.Name == "Deserialize" && m.Parameters.Length == 1 && m.Parameters[0].Type.ToDisplayString() == "byte[]"))
            {
                source.XmlSummary($"Deserializes byte data into <see cref=\"{classSymbol.Name}\"/> packet.\n<b>AUTOGENERATED</b>");
                source.XmlParam("data", "Data used to populate the packet.");
                source.XmlReturns("Deserialized packet.");

                source.Method($"public static {className} Deserialize(byte[] data)");
                source.Line("using var stream = new MinecraftStream(data);");
                source.Line("return Deserialize(stream);");
                source.EndScope();
            }

            // Deserialize(MinecraftStream stream)
            if (!methods.Any(m => m.Name == "Deserialize" && m.Parameters.Length == 1 && m.Parameters[0].Type.Name == "MinecraftStream"))
            {
                source.Line();
                source.XmlSummary($"Deserializes data from a <see cref=\"MinecraftStream\"/> into <see cref=\"{classSymbol.Name}\"/> packet.\n<b>AUTOGENERATED</b>");
                source.XmlParam("stream", "Stream that is read from to populate the packet.");
                source.XmlReturns("Deserialized packet.");

                source.Method($"public static {className} Deserialize(MinecraftStream stream)");
                source.Line($"var packet = new {className}();");
                source.Line("packet.Populate(stream);");
                source.Line("return packet;");
                source.EndScope();
            }
        }

        bodySource = CodeBuilder.WithIndentationOf(source.Indentation + 1);

        if (serverbound && TryCreatePopulateMethod(bodySource, fields, syntaxProvider))
        {
            // Populate(byte[] data)
            if (!methods.Any(m => m.Name == "Populate" && m.Parameters.Length == 1 && m.Parameters[0].Type.ToDisplayString() == "byte[]"))
            {
                source.Line();
                source.XmlSummary($"Populates this packet with data from a <see cref=\"byte\"/>[] buffer.");
                source.XmlParam("data", "Data used to populate this packet.");
                source.Method("public void Populate(byte[] data)");
                source.Line("using var stream = new MinecraftStream(data);");
                source.Line("Populate(stream);");
                source.EndScope();
            }

            // Populate(MinecraftStream stream)
            if (!methods.Any(m => m.Name == "Populate" && m.Parameters.Length == 1 && m.Parameters[0].Type.Name == "MinecraftStream"))
            {
                source.Line();
                source.XmlSummary("Populates this packet with data from a <see cref=\"MinecraftStream\"/>.");
                source.XmlParam("stream", "Stream used to populate this packet.");

                source.Method("public void Populate(MinecraftStream stream)");
                source.Append(bodySource);
                source.EndScope();
            }
        }

        bodySource.Clear();

        source.EndScope(); // End of type

        return source.ToString();
    }

    private bool TryCreateSerializationMethod(CodeBuilder builder, List<Property> properties, SyntaxProvider syntaxProvider)
    {
        string streamName = "packetStream";

        builder.Line($"using var {streamName} = new MinecraftStream();");
        foreach (Property property in properties)
        {
            if (property.IsCollection)
            {
                if (!TrySerializePropertyCollection(streamName, property, properties, builder, syntaxProvider))
                    return false;
            }
            else
            {
                if (!TrySerializeProperty(streamName, property, properties, builder, syntaxProvider))
                    return false;
            }
        }
        builder.Line();
        builder.Line("stream.Lock.Wait();");
        builder.Line($"stream.WriteVarInt(Id.GetVarIntLength() + (int){streamName}.Length);");
        builder.Line("stream.WriteVarInt(Id);");
        builder.Line($"{streamName}.Position = 0;");
        builder.Line($"{streamName}.CopyTo(stream);");
        builder.Line("stream.Lock.Release();");
        return true;
    }

    private bool TrySerializePropertyCollection(string streamName, Property property, List<Property> properties, CodeBuilder builder, SyntaxProvider syntaxProvider)
    {
        // If there is a method for writing the whole collection, use it instead
        if (syntaxProvider.Methods.TryGetWriteMethod(property, collection: true, out Method collectionWriteMethod))
            return TrySerializeProperty(streamName, property, properties, builder, syntaxProvider, collectionWriteMethod);

        syntaxProvider.Methods.TryGetWriteMethod(varInt, out Method writeMethod);

        var context = new MethodBuildingContext(streamName, property.Name, property, properties, builder, writeMethod, syntaxProvider.Methods, syntaxProvider.Context);

        if (property.Writing.Execute(context))
            return true;

        // Attributes behavior
        for (int i = 0; i < property.Attributes.Length; i++)
        {
            if (property.Attributes[i].ModifyCollectionPrefixSerialization(context))
                goto LOOP;
        }

        // Default behavior
        if (writeMethod is null)
        {
            ReportMissingMethod(syntaxProvider, property, "serialization");
            return false;
        }
        builder.Line($"{streamName}.{writeMethod}({property}.{property.Length});");

    LOOP:
        property.Written.Execute(context);

        // Begin the for loop
        builder.Statement($"for (int i = 0; i < {property}.{property.Length}; i++)");
        syntaxProvider.Methods.TryGetWriteMethod(property, out writeMethod);
        context = new MethodBuildingContext(streamName, property.Name + "[i]", property, properties, builder, writeMethod, syntaxProvider.Methods, syntaxProvider.Context);

        // Attributes behavior
        for (int i = 0; i < property.Attributes.Length; i++)
        {
            if (property.Attributes[i].ModifySerialization(context))
                goto END_LOOP;
        }

        // Default behavior
        if (writeMethod is null)
        {
            ReportMissingMethod(syntaxProvider, property, "serialization");
            return false;
        }
        builder.Line($"{streamName}.{writeMethod}({property}[i]);");

    END_LOOP:
        builder.EndScope();

        property.Written.Execute(context);
        return true;
    }

    private bool TrySerializeProperty(string streamName, Property property, List<Property> properties, CodeBuilder builder, SyntaxProvider syntaxProvider, Method writeMethod = null)
    {
        if (writeMethod is null)
            syntaxProvider.Methods.TryGetWriteMethod(property, out writeMethod);

        var context = new MethodBuildingContext(streamName, property.Name, property, properties, builder, writeMethod, syntaxProvider.Methods, syntaxProvider.Context);

        if (property.Writing.Execute(context))
            return true;

        // Attributes behavior
        for (int i = 0; i < property.Attributes.Length; i++)
        {
            if (property.Attributes[i].ModifySerialization(context))
            {
                property.Written.Execute(context);
                return true;
            }
        }

        // Default behavior
        if (writeMethod is null)
        {
            ReportMissingMethod(syntaxProvider, property, "serialization");
            return false;
        }
        builder.Line($"{streamName}.{writeMethod}({property});");

        property.Written.Execute(context);
        return true;
    }

    private bool TryCreatePopulateMethod(CodeBuilder builder, List<Property> properties, SyntaxProvider syntaxProvider)
    {
        return TryCreateReadingMethod(builder, properties, syntaxProvider);
    }

    private bool TryCreateReadingMethod(CodeBuilder builder, List<Property> properties, SyntaxProvider syntaxProvider)
    {
        string streamName = "stream";

        foreach (Property property in properties)
        {
            if (property.IsCollection)
            {
                if (!TryReadPropertyCollection(streamName, property.Name, property, properties, builder, syntaxProvider))
                    return false;
            }
            else
            {
                if (!TryReadProperty(streamName, property.Name, property, properties, builder, syntaxProvider))
                    return false;
            }
        }

        return true;
    }

    private bool TryReadPropertyCollection(string streamName, string dataName, Property property, List<Property> properties, CodeBuilder builder, SyntaxProvider syntaxProvider)
    {
        // If there is a method for writing the whole collection, use it instead
        if (syntaxProvider.Methods.TryGetReadMethod(property, collection: true, out Method collectionReadMethod))
            return TryReadProperty(streamName, dataName, property, properties, builder, syntaxProvider, collectionReadMethod);

        syntaxProvider.Methods.TryGetReadMethod(varInt, out Method readMethod);
        var context = new MethodBuildingContext(streamName, dataName, property, properties, builder, readMethod, syntaxProvider.Methods, syntaxProvider.Context);

        if (property.Reading.Execute(context))
            return true;

        // Attributes behavior
        for (int i = 0; i < property.Attributes.Length; i++)
        {
            if (property.Attributes[i].ModifyCollectionPrefixDeserialization(context))
                goto LOOP;
        }

        // Default behavior
        if (readMethod is null)
        {
            ReportMissingMethod(syntaxProvider, property, "deserialization");
            return false;
        }
        string getLength = $"{streamName}.{readMethod}()";
        builder.Line($"{dataName} = {property.NewCollection(getLength)};");

    LOOP:
        builder.Statement($"for (int i = 0; i < {dataName}.{property.Length}; i++)");

        context = new MethodBuildingContext(streamName, dataName + "[i]", property, properties, builder, readMethod, syntaxProvider.Methods, syntaxProvider.Context);

        // Attributes behavior
        for (int i = 0; i < property.Attributes.Length; i++)
        {
            if (property.Attributes[i].ModifyDeserialization(context))
                goto END_LOOP;
        }

        // Default behavior
        if (readMethod is null)
        {
            ReportMissingMethod(syntaxProvider, property, "deserialization");
            return false;
        }
        builder.Line($"{dataName}[i] = {streamName}.{readMethod}();");

    END_LOOP:
        builder.EndScope();

        property.Read.Execute(context);
        return true;
    }

    private bool TryReadProperty(string streamName, string dataName, Property property, List<Property> properties, CodeBuilder builder, SyntaxProvider syntaxProvider, Method readMethod = null)
    {
        if (readMethod is null)
            syntaxProvider.Methods.TryGetReadMethod(property, out readMethod);

        var context = new MethodBuildingContext(streamName, dataName, property, properties, builder, readMethod, syntaxProvider.Methods, syntaxProvider.Context);

        if (property.Reading.Execute(context))
            return true;

        // Attributes behavior
        for (int i = 0; i < property.Attributes.Length; i++)
        {
            if (property.Attributes[i].ModifyDeserialization(context))
            {
                property.Read.Execute(context);
                return true;
            }
        }

        // Default behavior
        if (readMethod is null)
        {
            ReportMissingMethod(syntaxProvider, property, "deserialization");
            return false;
        }
        builder.Line($"{dataName} = {streamName}.{readMethod}();");

        property.Read.Execute(context);
        return true;
    }

    private void ReportMissingMethod(SyntaxProvider syntaxProvider, Property property, string process)
    {
        syntaxProvider.Context.ReportDiagnostic(DiagnosticSeverity.Warning, $"{property} ({property.Type};{property.CollectionType}) has no {process} method associated with its type", property.DeclarationSyntax);
    }

    private class SyntaxProvider : ExecutionSyntaxProvider<MemberDeclarationSyntax>
    {
        public MethodsRegistry Methods { get; } = new();

        protected override bool HandleNode(MemberDeclarationSyntax node)
        {
            if (node is MethodDeclarationSyntax methodDeclaration)
            {
                Methods.Offer(Context, methodDeclaration);
            }

            // Save all fields and properties marked with FieldAttribute
            return (node is FieldDeclarationSyntax field && HasFieldAttribute(field.AttributeLists)) ||
                   (node is PropertyDeclarationSyntax property && HasFieldAttribute(property.AttributeLists));
        }

        private static bool HasFieldAttribute(SyntaxList<AttributeListSyntax> attributeLists)
        {
            return attributeLists.SelectMany(list => list.Attributes).Any(attribute => Vocabulary.AttributeNamesEqual(attribute.Name.ToString(), Vocabulary.FieldAttribute));
        }
    }
}

internal static class Extensions
{
    public static SyntaxNode GetRoot(this SyntaxNode syntaxNode)
    {
        while (syntaxNode.Parent is not null)
        {
            syntaxNode = syntaxNode.Parent;
        }
        return syntaxNode;
    }

    public static bool Execute(this PreactionCallback callback, MethodBuildingContext context)
    {
        if (callback is null)
            return false;

        foreach (PreactionCallback subcallback in callback.GetInvocationList())
        {
            if (subcallback(context))
                return true;
        }

        return false;
    }

    public static void Execute(this PostactionCallback callback, MethodBuildingContext context)
    {
        callback?.Invoke(context);
    }
}
