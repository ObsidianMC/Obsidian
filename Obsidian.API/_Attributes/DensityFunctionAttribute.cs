namespace Obsidian.API;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class DensityFunctionAttribute(string type) : Attribute
{
    public string Type { get; } = type;
}
