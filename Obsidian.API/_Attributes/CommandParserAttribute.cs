namespace Obsidian.API;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class CommandParserAttribute(string identifier) : Attribute
{
    public string Identifier { get; } = identifier;
}
