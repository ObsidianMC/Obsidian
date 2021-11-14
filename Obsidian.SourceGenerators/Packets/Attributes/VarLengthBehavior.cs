using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Obsidian.SourceGenerators.Packets.Attributes;

internal sealed class VarLengthBehavior : AttributeBehaviorBase
{
    public override string Name => Vocabulary.VarLengthAttribute;
    public override AttributeFlags Flag => AttributeFlags.VarLength;

    public VarLengthBehavior(AttributeSyntax attributeSyntax) : base(attributeSyntax)
    {
    }

    public override bool Matches(AttributeOwner other)
    {
        return other.Flags.HasFlag(Flag);
    }
}
