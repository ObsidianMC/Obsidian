namespace Obsidian.API;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class SurfaceConditionAttribute(string identifier) : Attribute
{
    public string Identifier { get; } = identifier;
}
