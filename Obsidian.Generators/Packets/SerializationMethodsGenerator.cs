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
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new ExecutionSyntaxProvider<MemberDeclarationSyntax>(member => (member is FieldDeclarationSyntax || member is PropertyDeclarationSyntax) && member.AttributeLists.Count > 0));
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxReceiver is not ExecutionSyntaxProvider<MemberDeclarationSyntax> memberProvider)
                return;

            Compilation compilation = context.Compilation;

            INamedTypeSymbol attributeSymbol = compilation.GetTypeByMetadataName("Obsidian.Serializer.Attributes.FieldAttribute");

            var memberSymbols = new List<(TypeSyntax type, ISymbol symbol)>();
            foreach (MemberDeclarationSyntax member in memberProvider.WithContext(context).GetSyntaxNodes())
            {
                SemanticModel model = compilation.GetSemanticModel(member.SyntaxTree);
                if (member is FieldDeclarationSyntax field)
                {
                    foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
                    {
                        ISymbol symbol = model.GetDeclaredSymbol(variable);
                        if (symbol.GetAttributes().Any(attribute => attribute.AttributeClass.Equals(attributeSymbol, SymbolEqualityComparer.Default)))
                        {
                            memberSymbols.Add((field.Declaration.Type, symbol));
                        }
                    }
                }
                else if (member is PropertyDeclarationSyntax property)
                {
                    ISymbol symbol = model.GetDeclaredSymbol(member);
                    memberSymbols.Add((property.Type, symbol));
                }
            }

            foreach (var group in memberSymbols.GroupBy(member => member.symbol.ContainingType))
            {
                string classSource = ProcessClass(group.Key, group.ToList(), attributeSymbol, context);
                context.AddSource($"{group.Key.Name}_Serialization.cs", SourceText.From(classSource, Encoding.UTF8));
            }    
        }

        private string ProcessClass(INamedTypeSymbol classSymbol, List<(TypeSyntax type, ISymbol symbol)> members, ISymbol attributeSymbol, GeneratorExecutionContext context)
        {
            string @namespace = classSymbol.ContainingNamespace.ToDisplayString();

            var source = new StringBuilder($@"
namespace {@namespace}
{{
    public partial class {classSymbol.Name}
    {{
");
            source.Append($"public const int memberCount = {members.Count};");

            source.Append("} }");
            return source.ToString();
        }
    }
}
