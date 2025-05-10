namespace Obsidian.API;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class SurfaceRuleAttribute(string identifier) : Attribute
{
    public string Identifier { get; } = identifier;
}
