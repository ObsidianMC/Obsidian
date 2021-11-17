namespace Obsidian.SourceGenerators.Packets.Attributes;

internal sealed class DataFormatBehavior : AttributeBehaviorBase
{
    public override string Name => Vocabulary.DataFormatAttribute;
    public override AttributeFlags Flag => AttributeFlags.DataFormat;

    public string Type { get; }

    public DataFormatBehavior(AttributeSyntax attributeSyntax) : base(attributeSyntax)
    {
        TryEvaluateTypeArgument(out string type);

        Type = type;
    }

    public override bool Matches(AttributeOwner other)
    {
        return other.Flags.HasFlag(Flag) &&
            other.TryGetAttribute(out DataFormatBehavior format) &&
            format.Type == Type;
    }
}
