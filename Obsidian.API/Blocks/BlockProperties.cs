using Obsidian.API.Inventory;

namespace Obsidian.API.Blocks;

/// <summary>
/// What a plugin's block is like: its states, how it collides, lights and breaks, and what it does when used.
/// </summary>
public sealed class BlockProperties
{
    /// <summary>
    /// The properties the block's states vary by; none gives the block a single state. They can't change while the
    /// server runs, since clients number states by them.
    /// </summary>
    public IReadOnlyList<StateProperty> StateProperties { get; init; } = [];

    /// <summary>
    /// The default state's value of each property, by name; a property not listed defaults to its first value.
    /// </summary>
    public IReadOnlyDictionary<string, string> DefaultState { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Whether the block is a full cube that entities collide with, like stone; otherwise entities pass through it,
    /// like a flower.
    /// </summary>
    public bool HasCollision { get; init; } = true;

    /// <summary>
    /// Whether the block hides what's behind it and blocks light, like stone rather than glass. Only blocks with
    /// collision can be opaque.
    /// </summary>
    public bool Opaque { get; init; } = true;

    /// <summary>
    /// How long the block takes to break, like vanilla's destroy time: 1.5 for stone, 0 to break instantly, -1 to make
    /// it unbreakable.
    /// </summary>
    public float Hardness { get; init; } = 1.5f;

    /// <summary>
    /// How well the block resists explosions, like vanilla's explosion resistance: 6 for stone, 1200 for obsidian.
    /// Null gives it its <see cref="Hardness"/>, as vanilla's <c>strength</c> does, or bedrock's resistance when it's
    /// unbreakable.
    /// </summary>
    public float? ExplosionResistance { get; init; }

    /// <summary>
    /// Whether a player must use the right tool for the block to drop loot. Explosions bypass this check.
    /// </summary>
    public bool RequiresCorrectTool { get; init; }

    /// <summary>
    /// The drop table's identifier. Null uses &lt;namespace&gt;:blocks/&lt;path&gt;, falling back to the block's
    /// own item when that table is absent. An explicit missing table drops nothing.
    /// </summary>
    public string? LootTable { get; init; }

    /// <summary>
    /// The light level the block gives off, from 0 to 15.
    /// </summary>
    public int LightEmission { get; init; }

    /// <summary>
    /// What the block does when a player uses it, or null for nothing.
    /// </summary>
    public IBlockBehavior? Behavior { get; init; }

    /// <summary>
    /// The trees the block grows into, which makes it a sapling: it grows in light on random ticks and with bone meal
    /// like vanilla's saplings. Null for a block that doesn't grow. Like <see cref="Behavior"/>, it's the plugin's:
    /// the block stops growing when the plugin unloads, and a reload replaces it.
    /// </summary>
    public TreeGrower? TreeGrower { get; init; }

    /// <summary>
    /// The properties of the block's item, which has the block's identifier, places the block and is what the block
    /// drops; null for a block without an item.
    /// </summary>
    public ItemProperties? Item { get; init; } = new();
}
