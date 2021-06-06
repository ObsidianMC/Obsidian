using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Obsidian.Generators.Packets
{
    [Generator]
    public class SerializationMethodsGenerator : ISourceGenerator
    {
        private const string fieldAttribute = "Field";
        private const string readMethodAttribute = "ReadMethod";
        private const string writeMethodAttribute = "WriteMethod";
        private const string absoluteAttribute = "Absolute";
        private const string actualTypeAttribute = "ActualType";
        private const string fixedLengthAttribute = "FixedLength";
        private const string varLengthAttribute = "VarLength";
        private const string clientOnly = "ClientOnlyAttribute";
        private const string serverOnly = "ServerOnlyAttribute";
        private const string countType = "CountType";

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new SyntaxProvider());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxReceiver is not SyntaxProvider syntaxProvider)
                return;

            Compilation compilation = context.Compilation;

            // Get all packet fields
            var fieldSymbols = new List<Field>();
            foreach (MemberDeclarationSyntax member in syntaxProvider.WithContext(context).GetSyntaxNodes())
            {
                // Get FieldAttribute
                AttributeSyntax attribute = member.AttributeLists.SelectMany(list => list.Attributes).FirstOrDefault(attribute => attribute.Name.ToString() == fieldAttribute);
                if (attribute is null)
                    continue;

                SemanticModel model = compilation.GetSemanticModel(member.SyntaxTree);
                if (member is FieldDeclarationSyntax field)
                {
                    foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
                    {
                        ISymbol symbol = model.GetDeclaredSymbol(variable);
                        fieldSymbols.Add(new Field(field.Declaration.Type, symbol, field, attribute));
                    }
                }
                else if (member is PropertyDeclarationSyntax property)
                {
                    ISymbol symbol = model.GetDeclaredSymbol(member);
                    fieldSymbols.Add(new Field(property.Type, symbol, property, attribute));
                }
            }

            // Generate partial classes
            foreach (var group in fieldSymbols.GroupBy(field => field.Symbol.ContainingType))
            {
                var @class = group.Key;
                var fields = group.ToList();
                
                if (@class.IsStatic || @class.DeclaredAccessibility != Accessibility.Public)
                {
                    context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.ContainingTypeNotViable, @class.Locations.First(), @class.Name));
                    continue;
                }

                string classSource = ProcessClass(@class, fields, syntaxProvider);
                context.AddSource($"{@class.Name}_Serialization.cs", SourceText.From(classSource, Encoding.UTF8));
            }    
        }

        private string ProcessClass(INamedTypeSymbol classSymbol, List<Field> fields, SyntaxProvider syntaxProvider)
        {
            fields.Sort((a, b) => a.Index.CompareTo(b.Index));
            
            string @namespace = classSymbol.ContainingNamespace.ToDisplayString();
            string className = classSymbol.IsGenericType ? $"{classSymbol.Name}<{string.Join(", ", classSymbol.TypeParameters.Select(parameter => parameter.Name))}>" : classSymbol.Name;

            var attributes = classSymbol.GetAttributes();
            bool isReadOnly = attributes.Any(attribute => attribute.AttributeClass.Name == serverOnly);
            bool isWriteOnly = attributes.Any(attribute => attribute.AttributeClass.Name == clientOnly);

            var methods = classSymbol.GetMembers().OfType<IMethodSymbol>();

            var source = new CodeBuilder();

            foreach (SyntaxReference declaration in classSymbol.DeclaringSyntaxReferences)
            {
                SyntaxNode root = declaration.GetSyntax().GetRoot();
                foreach (SyntaxNode usingDirective in root.DescendantNodes().Where(node => node is UsingDirectiveSyntax))
                {
                    source.Append(usingDirective.GetText().ToString());
                }
            }
            source.Using("Obsidian.Net");
            source.Using("Obsidian.Utilities");
            source.Using("System.Runtime.CompilerServices");
            source.Line();

            source.Namespace(@namespace);
            source.Type(classSymbol);

            var bodySource = CodeBuilder.WithIndentationOf(source.Indentation + 1);

            // Serialize(MinecraftStream stream)
            bool createSerializationMethod =
                !isReadOnly
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

            bool createDeserializationMethod =
                !isWriteOnly
                && !classSymbol.IsAbstract
                && !methods.Any(m => m.Name == "Deserialize" && m.Parameters.Length == 1 && m.Parameters[0].Type.Name is "byte[]" or "MinecraftStream")
                && TryCreateDeserializationMethod(bodySource, className, fields, syntaxProvider);
            if (createDeserializationMethod)
            {
                if (createSerializationMethod)
                    source.Line();
                
                // Deserialize(byte[] data)
                source.XmlSummary($"Deserializes byte data into <see cref=\"{classSymbol.Name}\"/> packet.\n<b>AUTOGENERATED</b>");
                source.XmlParam("data", "Data used to populate the packet.");
                source.XmlReturns("Deserialized packet.");

                source.Method($"public static {className} Deserialize(byte[] data)");
                source.Line("using var stream = new MinecraftStream(data);");
                source.Line("return Deserialize(stream);");
                source.EndScope();

                source.Line();

                // Deserialize(MinecraftStream stream)
                source.XmlSummary($"Deserializes data from a <see cref=\"MinecraftStream\"/> into <see cref=\"{classSymbol.Name}\"/> packet.\n<b>AUTOGENERATED</b>");
                source.XmlParam("stream", "Stream that is read from to populate the packet.");
                source.XmlReturns("Deserialized packet.");

                source.Method($"public static {className} Deserialize(MinecraftStream stream)");
                source.Append(bodySource);
                source.EndScope();
            }

            bodySource = CodeBuilder.WithIndentationOf(source.Indentation + 1);

            bool shouldPopulate =
                !isWriteOnly
                && !classSymbol.IsAbstract
                && !methods.Any(m => m.Name == "Populate" && m.Parameters.Length == 1 && m.Parameters[0].Type.Name is "byte[]" or "MinecraftStream")
                && TryCreatePopulateMethod(bodySource, className, fields, syntaxProvider);
            if (shouldPopulate)
            {
                if (createDeserializationMethod)
                    source.Line();

                // Populate(byte[] data)
                source.XmlSummary($"Populates this packet with data from a <see cref=\"byte\"/>[] buffer.");
                source.XmlParam("data", "Data used to populate this packet.");

                source.Method("public void Populate(byte[] data)");
                source.Line("using var stream = new MinecraftStream(data);");
                source.Line("Populate(stream);");
                source.EndScope();

                source.Line();

                // Populate(MinecraftStream stream)
                source.XmlSummary("Populates this packet with data from a <see cref=\"MinecraftStream\"/>.");
                source.XmlParam("stream", "Stream used to populate this packet.");

                source.Method("public void Populate(MinecraftStream stream)");
                source.Append(bodySource);
                source.EndScope();
            }

            bodySource.Clear();

            source.EndScope(); // EOF type
            source.EndScope(); // EOF namespace

            return source.ToString();
        }

        private bool TryCreateSerializationMethod(CodeBuilder builder, List<Field> fields, SyntaxProvider syntaxProvider)
        {
            builder.Line("using var packetStream = new MinecraftStream();");
            foreach (var field in fields)
            {
                string elementType = field.TypeName, elementName = field.Name;
                if (field.IsArray)
                {
                    elementName += "[i]";
                    string lengthProperty;
                    if (field.TypeName.EndsWith("[]"))
                    {
                        lengthProperty = "Length";
                        elementType = field.TypeName.Substring(0, field.TypeName.Length - 2);
                    }
                    else
                    {
                        lengthProperty = "Count";
                        elementType = field.TypeName.Substring(5, field.TypeName.Length - 6);
                    }

                    if (field.IsVarLength)
                        elementType += "_Var";
                    if (field.IsAbsolute)
                        elementType += "_Abs";

                    if (field.FixedLength < 0)
                    {
                        if (field.CountType is null)
                        {
                            builder.Line($"packetStream.WriteVarInt({field.Name}.{lengthProperty});");
                        }
                        else
                        {
                            if (!syntaxProvider.WriteMethods.TryGetValue(field.CountType, out string writeCountMethod))
                            {
                                // CountType has no write method
                                syntaxProvider.Context.ReportDiagnostic(DiagnosticSeverity.Warning, $"{field.Name} ({field.TypeName})({elementType}) has no serialization method associated with its count type", field.Declaration);
                                return false;
                            }

                            builder.Line($"packetStream.{writeCountMethod}(({field.CountType}){field.Name}.{lengthProperty});");
                        }
                    }

                    builder.Statement($"for (int i = 0; i < {field.Name}.{lengthProperty}; i++)");
                }

                if (field.OriginalType is not null)
                {
                    if (field.IsGeneric)
                    {
                        string tempName = $"temp{field.Name}";
                        builder.Line($"var {tempName} = {elementName};");
                        elementName = $"System.Runtime.CompilerServices.Unsafe.As<{field.ActualType}, {field.OriginalType}>(ref {tempName})";
                    }
                    else
                    {
                        elementName = $"({field.OriginalType}){elementName}";
                    }
                }

                if (TryGetMethod(elementType, syntaxProvider.WriteMethods, out string methodName))
                {
                    builder.Line($"packetStream.{methodName}({elementName});");
                }
                else
                {
                    // Creating serialization method failed
                    syntaxProvider.Context.ReportDiagnostic(DiagnosticSeverity.Warning, $"{field.Name} ({field.TypeName})({elementType}) has no serialization method associated with it", field.Declaration);
                    return false;
                }

                if (field.IsArray)
                {
                    // End the for loop
                    builder.EndScope();
                }
            }
            builder.Line("stream.Lock.Wait();");
            builder.Line("stream.WriteVarInt(Id.GetVarIntLength() + (int)packetStream.Length);");
            builder.Line("stream.WriteVarInt(Id);");
            builder.Line("packetStream.Position = 0;");
            builder.Line("packetStream.CopyTo(stream);");
            builder.Line("stream.Lock.Release();");
            return true;
        }

        private bool TryCreateDeserializationMethod(CodeBuilder builder, string className, List<Field> fields, SyntaxProvider syntaxProvider)
        {
            builder.Line($"var packet = new {className}();");
            foreach (var field in fields)
            {
                string elementType = field.TypeName, elementName = field.Name;
                if (field.IsArray)
                {
                    elementName += "[i]";
                    string lengthProperty;
                    if (field.TypeName.EndsWith("[]"))
                    {
                        lengthProperty = "Length";
                        elementType = field.TypeName.Substring(0, field.TypeName.Length - 2);
                    }
                    else
                    {
                        lengthProperty = "Count";
                        elementType = field.TypeName.Substring(5, field.TypeName.Length - 6);
                    }

                    if (field.IsVarLength)
                        elementType += "_Var";
                    if (field.IsAbsolute)
                        elementType += "_Abs";

                    string countValue;
                    if (field.FixedLength >= 0)
                    {
                        countValue = field.FixedLength.ToString();
                    }
                    else if (field.CountType is not null)
                    {
                        if (!syntaxProvider.ReadMethods.TryGetValue(field.CountType, out string readCountMethod))
                        {
                            // CountType has no read method
                            syntaxProvider.Context.ReportDiagnostic(DiagnosticSeverity.Warning, $"{field.Name} ({field.TypeName})({elementType}) has no deserialization method associated with its count type", field.Declaration);
                            return false;
                        }

                        countValue = $"stream.{readCountMethod}();";
                    }
                    else
                    {
                        countValue = "stream.ReadVarInt()";
                    }
                    builder.Line(field.TypeName.EndsWith("[]") ? $"packet.{field.Name} = new {elementType}[{countValue}];" : $"packet.{field.Name} = new {field.TypeName}({countValue});");

                    builder.Statement($"for (int i = 0; i < packet.{field.Name}.{lengthProperty}; i++)");
                }

                if (TryGetMethod(elementType, syntaxProvider.ReadMethods, out string methodName))
                {
                    methodName = $"stream.{methodName}()";
                    
                    if (field.OriginalType is not null)
                    {
                        if (field.IsGeneric)
                        {
                            string tempName = $"temp{field.Name}";
                            builder.Line($"var {tempName} = {methodName};");
                            methodName = $"System.Runtime.CompilerServices.Unsafe.As<{field.ActualType}, {field.OriginalType}>(ref {tempName})";
                        }
                        else
                        {
                            methodName = $"({field.ActualType}){methodName}";
                        }
                    }

                    builder.Line($"packet.{elementName} = {methodName};");
                }
                else
                {
                    // Creating serialization method failed
                    syntaxProvider.Context.ReportDiagnostic(DiagnosticSeverity.Warning, $"{field.Name} ({field.TypeName})({elementType}) has no deserialization method associated with it", field.Declaration);
                    return false;
                }

                if (field.IsArray)
                {
                    // End the for loop
                    builder.EndScope();
                }
            }
            builder.Line("return packet;");
            return true;
        }

        private bool TryCreatePopulateMethod(CodeBuilder builder, string className, List<Field> fields, SyntaxProvider syntaxProvider)
        {
            foreach (var field in fields)
            {
                string elementType = field.TypeName, elementName = field.Name;
                if (field.IsArray)
                {
                    elementName += "[i]";
                    string lengthProperty;
                    if (field.TypeName.EndsWith("[]"))
                    {
                        lengthProperty = "Length";
                        elementType = field.TypeName.Substring(0, field.TypeName.Length - 2);
                    }
                    else
                    {
                        lengthProperty = "Count";
                        elementType = field.TypeName.Substring(5, field.TypeName.Length - 6);
                    }

                    if (field.IsVarLength)
                        elementType += "_Var";
                    if (field.IsAbsolute)
                        elementType += "_Abs";

                    string countValue;
                    if (field.FixedLength >= 0)
                    {
                        countValue = field.FixedLength.ToString();
                    }
                    else if (field.CountType is not null)
                    {
                        if (!syntaxProvider.ReadMethods.TryGetValue(field.CountType, out string readCountMethod))
                        {
                            // CountType has no read method
                            syntaxProvider.Context.ReportDiagnostic(DiagnosticSeverity.Warning, $"{field.Name} ({field.TypeName})({elementType}) has no deserialization method associated with its count type", field.Declaration);
                            return false;
                        }

                        countValue = $"stream.{readCountMethod}();";
                    }
                    else
                    {
                        countValue = "stream.ReadVarInt()";
                    }
                    builder.Line(field.TypeName.EndsWith("[]") ? $"{field.Name} = new {elementType}[{countValue}];" : $"{field.Name} = new {field.TypeName}({countValue});");

                    builder.Statement($"for (int i = 0; i < {field.Name}.{lengthProperty}; i++)");
                }

                if (TryGetMethod(elementType, syntaxProvider.ReadMethods, out string methodName))
                {
                    methodName = $"stream.{methodName}()";

                    if (field.OriginalType is not null)
                    {
                        if (field.IsGeneric)
                        {
                            string tempName = $"temp{field.Name}";
                            builder.Line($"var {tempName} = {methodName};");
                            methodName = $"System.Runtime.CompilerServices.Unsafe.As<{field.ActualType}, {field.OriginalType}>(ref {tempName})";
                        }
                        else
                        {
                            methodName = $"({field.ActualType}){methodName}";
                        }
                    }

                    builder.Line($"{elementName} = {methodName};");
                }
                else
                {
                    // Creating serialization method failed
                    syntaxProvider.Context.ReportDiagnostic(DiagnosticSeverity.Warning, $"{field.Name} ({field.TypeName})({elementType}) has no deserialization method associated with it", field.Declaration);
                    return false;
                }

                if (field.IsArray)
                {
                    // End the for loop
                    builder.EndScope();
                }
            }
            return true;
        }

        private bool TryGetMethod(string typeName, Dictionary<string, string> methodCollection, out string methodName)
        {
            return methodCollection.TryGetValue(typeName, out methodName);
        }

        private class SyntaxProvider : ExecutionSyntaxProvider<MemberDeclarationSyntax>
        {
            public Dictionary<string, string> WriteMethods { get; } = new Dictionary<string, string>();
            public Dictionary<string, string> ReadMethods { get; } = new Dictionary<string, string>();

            public SyntaxProvider()
            {
            }

            /// <summary>
            /// Decides whether a certain syntax node should be returned by <see cref="ExecutionSyntaxProvider{T}.GetSyntaxNodes"/>.
            /// </summary>
            protected override bool HandleNode(MemberDeclarationSyntax node)
            {
                // Handle all Read and Write methods
                if (node is MethodDeclarationSyntax methodDeclaration)
                {
                    var attributes = methodDeclaration.AttributeLists.SelectMany(list => list.Attributes);
                    var attribute = attributes.FirstOrDefault(attribute => attribute.Name.ToString() == readMethodAttribute || attribute.Name.ToString() == writeMethodAttribute);
                    if (attribute is not null)
                    {
                        string methodName = methodDeclaration.Identifier.Text;
                        string modifiers = string.Empty;
                        if (attributes.Any(attribute => attribute.Name.ToString() == varLengthAttribute))
                            modifiers += "_Var";
                        if (attributes.Any(attribute => attribute.Name.ToString() == absoluteAttribute))
                            modifiers += "_Abs";
                        if (attribute.Name.ToString() == readMethodAttribute)
                        {
                            ReadMethods[methodDeclaration.ReturnType.GetText().ToString().Split('.').Last().TrimEnd() + modifiers] = methodName;
                        }
                        else
                        {
                            WriteMethods[methodDeclaration.ParameterList.Parameters.First().Type.GetText().ToString().Split('.').Last().TrimEnd() + modifiers] = methodName;
                        }
                    }
                }

                // Handle all fields and properties that may be packet fields
                return (node is FieldDeclarationSyntax || node is PropertyDeclarationSyntax) && node.AttributeLists.Count > 0;
            }
        }

        private struct Field
        {
            public string Name { get; }
            public string TypeName { get; }
            public string OriginalType { get; }
            public string ActualType { get; }
            public string CountType { get; }
            public int Index { get; }
            public bool IsArray { get; }
            public bool IsAbsolute { get; }
            public bool IsGeneric { get; }
            public bool IsVarLength { get; }
            public int FixedLength { get; }
            public ISymbol Symbol { get; }
            public MemberDeclarationSyntax Declaration { get; }

            public Field(TypeSyntax type, ISymbol symbol, MemberDeclarationSyntax declaration, AttributeSyntax fieldAttribute)
            {
                string typeName = type.GetText().ToString().Split('.').Last().TrimEnd();
                TypeName = typeName;
                ActualType = typeName;
                IsArray = TypeName.EndsWith("[]") || TypeName.StartsWith("List<");
                Name = symbol.Name;
                Symbol = symbol;
                Declaration = declaration;
                Index = int.Parse(fieldAttribute.ArgumentList.Arguments.First().GetText().ToString());

                OriginalType = null;
                CountType = null;
                IsAbsolute = false;
                IsVarLength = false;
                FixedLength = -1;
                IsGeneric = symbol.ContainingType.TypeParameters.Any(genericParameter => genericParameter.Name == typeName);
                var attributes = declaration.AttributeLists.SelectMany(list => list.Attributes);
                foreach (var attribute in attributes)
                {
                    switch (attribute.Name.GetText().ToString())
                    {
                        case absoluteAttribute:
                            IsAbsolute = true;
                            break;

                        case varLengthAttribute:
                            IsVarLength = true;
                            break;

                        case fixedLengthAttribute:
                            FixedLength = int.Parse(attribute.ArgumentList.Arguments.First().GetText().ToString());
                            break;

                        case actualTypeAttribute:
                            TypeName = GetAttributeTypeArgument(attribute);
                            OriginalType = TypeName;
                            break;

                        case countType:
                            CountType = GetAttributeTypeArgument(attribute);
                            break;
                    }
                }

                if (!IsArray)
                {
                    if (IsVarLength)
                        TypeName += "_Var";
                    if (IsAbsolute)
                        TypeName += "_Abs";
                }
            }

            private static string GetAttributeTypeArgument(AttributeSyntax attribute)
            {
                var @typeof = attribute.DescendantNodes().FirstOrDefault(node => node is TypeOfExpressionSyntax) as TypeOfExpressionSyntax;
                return @typeof.Type.GetText().ToString().Split('.').Last();
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
    }
}
