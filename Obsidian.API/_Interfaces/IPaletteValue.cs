namespace Obsidian.API;

/// <summary>
/// A value that a chunk section palette can store by id.
/// </summary>
/// <typeparam name="TSelf">The implementing type.</typeparam>
public interface IPaletteValue<TSelf> where TSelf : IPaletteValue<TSelf>
{
    /// <summary>
    /// Creates the value with id <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The value's id.</param>
    /// <returns>The value.</returns>
    public static abstract TSelf Construct(int value);
}
