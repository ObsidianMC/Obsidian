using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Obsidian.SourceGenerators.Packets.Attributes
{
    internal sealed class ActualTypeBehavior : AttributeBehaviorBase
    {
        public override string Name => Vocabulary.ActualTypeAttribute;
        public override AttributeFlags Flag => AttributeFlags.ActualType;
        public string Type { get; }

        public ActualTypeBehavior(AttributeSyntax attributeSyntax) : base(attributeSyntax)
        {
            TryEvaluateTypeArgument(out string type);

            Type = type;
        }

        public override bool ModifySerialization(MethodBuildingContext context)
        {
            if (!context.MethodsRegistry.TryGetWriteMethod(context.Property.CloneWithType(Type), out Method writeMethod))
            {
                DiagnosticHelper.ReportDiagnostic(context.GeneratorContext, DiagnosticSeverity.Warning, "There is no write method for this type, attribute will be ignored.", syntax);
                return false;
            }

            if (context.Property.IsGeneric)
            {
                context.CodeBuilder.Line($"var temp = {context.DataName};");
                context.CodeBuilder.Line($"{context.StreamName}.{writeMethod}(Unsafe.As<{context.Property.Type}, {Type}>(ref temp));");
            }
            else
            {
                context.CodeBuilder.Line($"{context.StreamName}.{writeMethod}(({Type}){context.DataName});");
            }
            return true;
        }

        public override bool ModifyDeserialization(MethodBuildingContext context)
        {
            if (!context.MethodsRegistry.TryGetReadMethod(context.Property.CloneWithType(Type), out Method readMethod))
            {
                DiagnosticHelper.ReportDiagnostic(context.GeneratorContext, DiagnosticSeverity.Warning, "There is no read method for this type, attribute will be ignored.", syntax);
                return false;
            }

            if (context.Property.IsGeneric)
            {
                context.CodeBuilder.Line($"var temp = {context.StreamName}.{readMethod}();");
                context.CodeBuilder.Line($"{context.DataName} = Unsafe.As<{readMethod.Type}, {context.Property.Type}>(ref temp);");
            }
            else
            {
                context.CodeBuilder.Line($"{context.DataName} = ({context.Property.Type}){context.StreamName}.{readMethod}();");
            }
            return true;
        }
    }
}
