namespace Obsidian.SourceGenerators.Packets.Attributes;

internal sealed class FieldBehavior : AttributeBehaviorBase
{
    public override string Name => Vocabulary.FieldAttribute;
    public override AttributeFlags Flag => AttributeFlags.Field;

    public int Order { get; }

    public FieldBehavior(AttributeSyntax attributeSyntax) : base(attributeSyntax)
    {
        TryEvaluateIntArgument(out int order);

        Order = order;
    }
}
