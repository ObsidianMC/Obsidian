namespace Obsidian.API;

/// <summary>
/// Marks a class as the implementation of a surface rule type that worldgen data files name by their <c>type</c>.
/// </summary>
/// <param name="identifier">The rule type's identifier.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class SurfaceRuleAttribute(string identifier) : Attribute
{
    /// <summary>
    /// Gets the rule type's identifier.
    /// </summary>
    public string Identifier { get; } = identifier;
}
