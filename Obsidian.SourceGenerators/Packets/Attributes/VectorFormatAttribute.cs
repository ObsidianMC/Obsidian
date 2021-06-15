using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Obsidian.SourceGenerators.Packets.Attributes
{
    internal sealed class VectorFormatAttribute : AttributeBehaviorBase
    {
        public override string Name => Vocabulary.DataFormatAttribute;
        public override AttributeFlags Flag => AttributeFlags.DataFormat;

        public string Type { get; }

        public VectorFormatAttribute(AttributeSyntax attributeSyntax) : base(attributeSyntax)
        {
            TryEvaluateTypeArgument(out string type);

            Type = type;
        }

        public override bool Matches(AttributeOwner other)
        {
            return other.Flags.HasFlag(Flag) &&
                other.TryGetAttribute(out VectorFormatAttribute format) &&
                format.Type == Type;
        }
    }
}
