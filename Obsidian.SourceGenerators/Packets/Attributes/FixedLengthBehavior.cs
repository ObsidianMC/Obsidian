using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Obsidian.SourceGenerators.Packets.Attributes;

internal sealed class FixedLengthBehavior : AttributeBehaviorBase
{
    public override string Name => Vocabulary.FixedLengthAttribute;
    public override AttributeFlags Flag => AttributeFlags.FixedLength;

    public int Length { get; }

    public FixedLengthBehavior(AttributeSyntax attributeSyntax) : base(attributeSyntax)
    {
        TryEvaluateIntArgument(out int length);

        Length = length;
    }

    public override bool ModifyCollectionPrefixSerialization(MethodBuildingContext context)
    {
        if (Length < 0)
        {
            DiagnosticHelper.ReportDiagnostic(context.GeneratorContext, DiagnosticSeverity.Warning, "Length must be a non-negative number. Attribute will be ignored.", syntax);
            return false;
        }

        // Doesn't write length prefix
        return true;
    }

    public override bool ModifyCollectionPrefixDeserialization(MethodBuildingContext context)
    {
        if (Length < 0)
        {
            DiagnosticHelper.ReportDiagnostic(context.GeneratorContext, DiagnosticSeverity.Warning, "Length must be a non-negative number. Attribute will be ignored.", syntax);
            return false;
        }

        context.CodeBuilder.Line($"{context.DataName} = {context.Property.NewCollection(Length.ToString())}");
        return true;
    }
}
