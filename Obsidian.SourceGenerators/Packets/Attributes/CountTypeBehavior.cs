using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Obsidian.SourceGenerators.Packets.Attributes
{
    internal sealed class CountTypeBehavior : AttributeBehaviorBase
    {
        public override string Name => Vocabulary.CountTypeAttribute;
        public override AttributeFlags Flag => AttributeFlags.CountType;

        public string Type { get; }

        public CountTypeBehavior(AttributeSyntax attributeSyntax) : base(attributeSyntax)
        {
            TryEvaluateTypeArgument(out string type);

            Type = type;
        }

        public override bool ModifyCollectionPrefixSerialization(MethodBuildingContext context)
        {
            if (!context.MethodsRegistry.TryGetWriteMethod(context.Property.CloneWithType(Type), out Method writeMethod))
            {
                DiagnosticHelper.ReportDiagnostic(context.GeneratorContext, DiagnosticSeverity.Warning, "There is no write method for this type, attribute will be ignored.", syntax);
                return false;
            }

            context.CodeBuilder.Line($"{context.StreamName}.{writeMethod}(({Type}){context.DataName}.{context.Property.Length});");
            return true;
        }

        public override bool ModifyCollectionPrefixDeserialization(MethodBuildingContext context)
        {
            if (!context.MethodsRegistry.TryGetReadMethod(context.Property.CloneWithType(Type), out Method readMethod))
            {
                DiagnosticHelper.ReportDiagnostic(context.GeneratorContext, DiagnosticSeverity.Warning, "There is no read method for this type, attribute will be ignored.", syntax);
                return false;
            }

            string getLength = $"{context.StreamName}.{readMethod}()";
            context.CodeBuilder.Line($"{context.DataName} = {context.Property.NewCollection(getLength)};");
            return true;
        }
    }
}
