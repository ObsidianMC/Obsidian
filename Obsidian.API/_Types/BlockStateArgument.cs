namespace Obsidian.API;

/// <summary>
/// A block and some of its properties as a command names them, like <c>minecraft:oak_stairs[facing=east]</c>.
/// </summary>
/// <param name="Name">The block's identifier, with its namespace.</param>
/// <param name="Properties">The properties given; the others keep their defaults.</param>
public sealed record BlockStateArgument(string Name, IReadOnlyDictionary<string, string> Properties);
