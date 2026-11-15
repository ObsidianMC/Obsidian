namespace Obsidian.API;

/// <summary>
/// Names the vanilla command argument type, like <c>brigadier:bool</c>, that a partial argument parser class is sent
/// to clients as. A source generator implements the parser's <see cref="BaseArgumentParser.Identifier"/> and
/// <see cref="BaseArgumentParser.Id"/> from it.
/// </summary>
/// <param name="identifier">The argument type's identifier.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ArgumentParserAttribute(string identifier) : Attribute
{
    /// <summary>
    /// Gets the argument type's identifier.
    /// </summary>
    public string Identifier { get; } = identifier;
}
