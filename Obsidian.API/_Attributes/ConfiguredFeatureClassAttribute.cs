namespace Obsidian.API;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ConfiguredFeatureClassAttribute(string identifier) : Attribute
{
    public string Identifier { get; } = identifier;
}
