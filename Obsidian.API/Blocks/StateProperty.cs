namespace Obsidian.API.Blocks;

/// <summary>
/// A property a plugin's block varies by, like a lamp's <c>lit</c> or a crop's <c>age</c>: the block has one state per
/// combination of its properties' values.
/// </summary>
/// <remarks>
/// A client mod has to declare the same block with the same properties, and each property's values in the same order,
/// since clients number a block's states by them: <see cref="Bool"/>, <see cref="Int"/> and <see cref="Enum"/> order
/// them like NeoForge's and Fabric's boolean, integer and enum properties.
/// </remarks>
public sealed class StateProperty
{
    private StateProperty(string name, ImmutableArray<string> values)
    {
        if (name.Length == 0 || !name.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
            throw new ArgumentException($"{name} isn't a valid property name.", nameof(name));

        if (values.Length == 0 || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException($"{name} needs distinct values.", nameof(values));

        this.Name = name;
        this.Values = values;
    }

    /// <summary>
    /// The property's name, like <c>lit</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The property's values, in the order clients number states by.
    /// </summary>
    public ImmutableArray<string> Values { get; }

    /// <summary>
    /// A property that is <c>true</c> or <c>false</c>.
    /// </summary>
    public static StateProperty Bool(string name) => new(name, ["true", "false"]);

    /// <summary>
    /// A property that is a number from <paramref name="min"/> to <paramref name="max"/>.
    /// </summary>
    public static StateProperty Int(string name, int min, int max)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(min);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(max, min);

        return new(name, [.. Enumerable.Range(min, max - min + 1).Select(value => value.ToString())]);
    }

    /// <summary>
    /// A property that is one of <paramref name="values"/>, listed in the order the client mod's enum declares them.
    /// </summary>
    public static StateProperty Enum(string name, params string[] values) => new(name, [.. values]);
}
