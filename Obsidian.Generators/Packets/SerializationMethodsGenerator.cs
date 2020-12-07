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
                System.Diagnostics.Debug.WriteLine(@class.Name);
            }    
        }

        private string ProcessClass(INamedTypeSymbol classSymbol, List<Field> fields, SyntaxProvider syntaxProvider)
        {
            fields.Sort((a, b) => a.Index.CompareTo(b.Index));
            
            string @namespace = classSymbol.ContainingNamespace.ToDisplayString();
            string className = classSymbol.IsGenericType ? $"{classSymbol.Name}<{string.Join(", ", classSymbol.TypeParameters.Select(parameter => parameter.Name))}>" : classSymbol.Name;

            var source = new StringBuilder();

            foreach (SyntaxReference declaration in classSymbol.DeclaringSyntaxReferences)
            {
                SyntaxNode root = declaration.GetSyntax().GetRoot();
                foreach (SyntaxNode usingDirective in root.DescendantNodes().Where(node => node is UsingDirectiveSyntax))
                {
                    source.Append(usingDirective.GetText().ToString());
                }
            }

            var bodySource = new StringBuilder();
            source.Append($@"using Obsidian.Net;
using Obsidian.Util.Extensions;

namespace {@namespace}
{{
    public ");

            if (classSymbol.IsAbstract)
            {
                source.Append("abstract ");
            }

            source.Append("partial ");

            source.Append(classSymbol.IsValueType ? "struct " : "class ");

            source.Append(className);

            source.Append(@"
    {
");

            string classOffset = "\t\t";

            // Serialize(MinecraftStream stream)
            if (CreateSerializationMethod(bodySource, fields, syntaxProvider))
            {
                source.AppendXML("summary", $"Serializes data from this packet into <see cref=\"MinecraftStream\"/>.\n<b>AUTOGENERATED</b>");
                source.AppendXML("param", @"name=""stream""", "Target stream that this packet's data is written to.", true);
                source.Append($"{classOffset}public void Serialize(MinecraftStream stream)\n{classOffset}{{\n");
                source.Append(bodySource.ToString());
                source.Append($"{classOffset}}}\n\n");
            }
            bodySource.Clear();

            if (!classSymbol.IsAbstract && CreateDeserializationMethod(bodySource, className, fields, syntaxProvider))
            {
                // Deserialize(byte[] data)
                source.AppendXML("summary", $"Deserializes byte data into <see cref=\"{classSymbol.Name}\"/> packet.\n<b>AUTOGENERATED</b>");
                source.AppendXML("param", @"name=""data""", "Data used to populate the packet.", true);
                source.AppendXML("returns", "Deserialized packet.", true);
                source.Append($"{classOffset}public static {className} Deserialize(byte[] data)\n{classOffset}{{\n");
                source.AppendCode("using var stream = new MinecraftStream(data);");
                source.AppendCode("return Deserialize(stream);");
                source.Append($"{classOffset}}}\n\n");

                // Deserialize(MinecraftStream stream)
                source.AppendXML("summary", $"Deserializes data from <see cref=\"MinecraftStream\"/> into <see cref=\"{classSymbol.Name}\"/> packet.\n<b>AUTOGENERATED</b>");
                source.AppendXML("param", @"name=""stream""", "Stream that is read from to populate the packet.", true);
                source.AppendXML("returns", "Deserialized packet.", true);
                source.Append($"{classOffset}public static {className} Deserialize(MinecraftStream stream)\n{classOffset}{{\n");
                source.Append(bodySource.ToString());
                source.Append($"{classOffset}}}");
            }
            bodySource.Clear();

            source.Append(@"
    }
}
");

            return source.ToString();
        }

        private bool CreateSerializationMethod(StringBuilder builder, List<Field> fields, SyntaxProvider syntaxProvider)
        {
            builder.AppendCode("using var packetStream = new MinecraftStream();");
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
                        builder.AppendCode($"packetStream.WriteVarInt({field.Name}.{lengthProperty});");

                    builder.AppendCode($"for (int i = 0; i < {field.Name}.{lengthProperty}; i++)");
                    builder.AppendCode("{");
                    builder.Append('\t');
                }

                if (field.OriginalType is not null)
                {
                    if (field.IsGeneric)
                    {
                        elementName = $"({field.OriginalType})(object){elementName}";
                    }
                    else
                    {
                        elementName = $"({field.OriginalType}){elementName}";
                    }
                }

                if (TryGetMethod(elementType, syntaxProvider.WriteMethods, out string methodName))
                {
                    builder.AppendCode($"packetStream.{methodName}({elementName});");
                }
                else
                {
                    // creating serialization method failed
                    syntaxProvider.Context.ReportDiagnostic(DiagnosticDescriptors.Create(DiagnosticSeverity.Warning, $"{field.Name} ({field.TypeName})({elementType}) has no serialization method associated with it", field.Declaration));
                    return false;
                }

                if (field.IsArray)
                {
                    // end for loop
                    builder.AppendCode("}");
                }
            }
            builder.AppendCode("stream.Lock.Wait();");
            builder.AppendCode("stream.WriteVarInt(Id.GetVarIntLength() + (int)packetStream.Length);");
            builder.AppendCode("stream.WriteVarInt(Id);");
            builder.AppendCode("packetStream.Position = 0;");
            builder.AppendCode("packetStream.CopyTo(stream);");
            builder.AppendCode("stream.Lock.Release();");
            return true;
        }

        private bool CreateDeserializationMethod(StringBuilder builder, string className, List<Field> fields, SyntaxProvider syntaxProvider)
        {
            builder.AppendCode($"var packet = new {className}();");
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

                    string countValue = field.FixedLength >= 0 ? field.FixedLength.ToString() : "stream.ReadVarInt()";
                    builder.AppendCode(field.TypeName.EndsWith("[]") ? $"packet.{field.Name} = new {elementType}[{countValue}];" : $"packet.{field.Name} = new {field.TypeName}({countValue});");

                    builder.AppendCode($"for (int i = 0; i < packet.{field.Name}.{lengthProperty}; i++)");
                    builder.AppendCode("{");
                    builder.Append('\t');
                }

                if (TryGetMethod(elementType, syntaxProvider.ReadMethods, out string methodName))
                {
                    methodName = "stream." + methodName;
                    
                    if (field.OriginalType is not null)
                    {
                        if (field.IsGeneric)
                        {
                            methodName = $"({field.ActualType})(object){methodName}";
                        }
                        else
                        {
                            methodName = $"({field.ActualType}){methodName}";
                        }
                    }

                    builder.AppendCode($"packet.{elementName} = {methodName}();");
                }
                else
                {
                    // creating serialization method failed
                    syntaxProvider.Context.ReportDiagnostic(DiagnosticDescriptors.Create(DiagnosticSeverity.Warning, $"{field.Name} ({field.TypeName})({elementType}) has no deserialization method associated with it", field.Declaration));
                    return false;
                }

                if (field.IsArray)
                {
                    // end for loop
                    builder.AppendCode("}");
                }
            }
            builder.AppendCode("return packet;");
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
                            var @typeof = attribute.DescendantNodes().FirstOrDefault(node => node is TypeOfExpressionSyntax) as TypeOfExpressionSyntax;
                            TypeName = @typeof.Type.GetText().ToString().Split('.').Last();
                            OriginalType = TypeName;
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
        }
    }

    internal static class Extensions
    {
        private static readonly string prefix = "\t\t///";
        
        public static StringBuilder AppendXML(this StringBuilder stringBuilder, string type, string content, bool inline = false)
        {
            if (inline)
            {
                return stringBuilder.AppendLine($"{prefix} <{type}>{content.Replace('\n', ' ')}</{type}>");
            }
            else
            {
                return stringBuilder.AppendLine($"{prefix} <{type}>").AppendLine(string.Join("<br/>\n", content.Split('\n').Select(c => $"{prefix} {c}"))).AppendLine($"{prefix} </{type}>");
            }
        }

        public static StringBuilder AppendXML(this StringBuilder stringBuilder, string type, string attributes, string content, bool inline = false)
        {
            if (inline)
            {
                return stringBuilder.AppendLine($"{prefix} <{type} {attributes}>{content.Replace('\n', ' ')}</{type}>");
            }
            else
            {
                return stringBuilder.AppendLine($"{prefix} <{type} {attributes}>").AppendLine(string.Join("<br/>\n", content.Split('\n').Select(c => $"{prefix} {c}"))).AppendLine($"{prefix} </{type}>");
            }
        }

        public static StringBuilder AppendCode(this StringBuilder stringBuilder, string code)
        {
            return stringBuilder.AppendLine($"\t\t\t{code}");
        }

        public static StringBuilder AppendComment(this StringBuilder stringBuilder, string comment)
        {
            return stringBuilder.AppendLine($"\t\t\t// {comment}");
        }

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
