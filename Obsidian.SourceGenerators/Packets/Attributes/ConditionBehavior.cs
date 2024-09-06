namespace Obsidian.SourceGenerators.Packets.Attributes;

internal sealed class ConditionBehavior : AttributeBehaviorBase
{
    public override string Name => Vocabulary.ConditionAttribute;
    public override AttributeFlags Flag => AttributeFlags.Condition;

    public string Condition { get; }
    public bool Skip { get; set; }

    private static (object list, string condition) last;

    public ConditionBehavior(AttributeSyntax attributeSyntax) : base(attributeSyntax)
    {
        TryEvaluateStringArgument(out string condition);

        Condition = condition;
    }

    public override bool ModifySerialization(MethodBuildingContext context)
    {
        return OpenCondition(context);
    }

    public override bool ModifyDeserialization(MethodBuildingContext context)
    {
        return OpenCondition(context);
    }

    private bool OpenCondition(MethodBuildingContext context)
    {
        if (string.IsNullOrWhiteSpace(Condition))
        {
            return false;
        }

        if (Skip)
        {
            Skip = false;
            return false;
        }

        var endProperty = GetEndProperty(context);
        endProperty.Written += EndCondition;
        endProperty.Read += EndCondition;

        if (last.list == context.AllProperties && IsOpposite(last.condition, Condition))
        {
            context.CodeBuilder.Statement("else");
        }
        else
        {
            context.CodeBuilder.Statement($"if ({Condition})");
        }
        last = (context.AllProperties, Condition);

        return false;
    }

    private void EndCondition(MethodBuildingContext context)
    {
        context.Property.Written -= EndCondition;
        context.Property.Read -= EndCondition;

        context.CodeBuilder.EndScope();
    }

    private Property GetEndProperty(MethodBuildingContext context)
    {
        var sharedCondition = context.AllProperties
            .SkipWhile(prop => prop != context.Property)
            .Select(prop => new { Property = prop, Attribute = prop.TryGetAttribute(out ConditionBehavior? condition) ? condition : null })
            .TakeWhile(entry => entry.Attribute?.Condition == Condition);

        foreach (var shared in sharedCondition.Skip(1))
        {
            shared.Attribute!.Skip = true;
        }

        return sharedCondition.Last().Property;
    }

    private bool IsOpposite(string a, string b)
    {
        if (a.Length < b.Length)
            (a, b) = (b, a);

        return a.Length == b.Length + 1
            && a.StartsWith("!")
            && a.Substring(1) == b;
    }
}
