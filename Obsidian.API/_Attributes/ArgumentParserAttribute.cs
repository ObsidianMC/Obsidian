namespace Obsidian.API;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ArgumentParserAttribute(string identifier) : Attribute
{
    public string Identifier { get; } = identifier;
}
