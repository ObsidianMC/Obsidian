namespace Obsidian.API.Configuration;

/// <summary>
/// Settings of the <c>NewWorld</c> section: how an integrated server creates its world when the world folder has no
/// <c>level.dat</c> yet. They're ignored for existing worlds.
/// </summary>
public sealed class NewWorldConfiguration
{
    /// <summary>
    /// The world's display name; the folder name when empty.
    /// </summary>
    public string? LevelName { get; set; }

    /// <summary>
    /// The seed as the player typed it: a number is used as-is, other text is hashed like Java's <c>String.hashCode</c>,
    /// and an empty seed picks a random one.
    /// </summary>
    public string? Seed { get; set; }

    /// <summary>
    /// Vanilla's world preset without its namespace: <c>default</c> or <c>flat</c> (the others aren't supported yet).
    /// </summary>
    public string WorldType { get; set; } = "default";

    /// <summary>
    /// Vanilla's superflat layer string or preset id. Obsidian's superflat generator has fixed layers, so it's ignored.
    /// </summary>
    public string? FlatPreset { get; set; }

    public bool GenerateStructures { get; set; } = true;

    /// <summary>
    /// Stored in <c>level.dat</c>; Obsidian doesn't place the bonus chest.
    /// </summary>
    public bool BonusChest { get; set; }

    public GameMode GameMode { get; set; } = GameMode.Survival;

    public Difficulty Difficulty { get; set; } = Difficulty.Normal;

    public bool Hardcore { get; set; }

    /// <summary>
    /// Whether the world allows commands (cheats), which decides the local player's operator level.
    /// </summary>
    public bool AllowCommands { get; set; }

    /// <summary>
    /// Game rules by vanilla id without the <c>minecraft:</c> namespace (configuration keys can't contain colons), e.g.
    /// <c>keep_inventory</c> = <c>true</c>.
    /// </summary>
    public Dictionary<string, string> GameRules { get; set; } = [];
}
