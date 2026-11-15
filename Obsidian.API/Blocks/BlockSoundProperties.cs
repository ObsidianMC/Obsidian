namespace Obsidian.API.Blocks;

/// <summary>
/// A block's break, step, place, hit and fall events, with the base volume and pitch used by block interactions.
/// Declare the same sounds in ConfigureRegistry on both sides. Reloading replaces them; unloading uses stone.
/// </summary>
public sealed record BlockSoundProperties
{
    /// <summary>
    /// Gets the sound event played when the block breaks.
    /// </summary>
    public required SoundEvent Break { get; init; }
    /// <summary>
    /// Gets the sound event played when an entity steps on the block.
    /// </summary>
    public required SoundEvent Step { get; init; }
    /// <summary>
    /// Gets the sound event played when the block is placed.
    /// </summary>
    public required SoundEvent Place { get; init; }
    /// <summary>
    /// Gets the sound event played while the block is being mined.
    /// </summary>
    public required SoundEvent Hit { get; init; }
    /// <summary>
    /// Gets the sound event played when an entity lands on the block.
    /// </summary>
    public required SoundEvent Fall { get; init; }
    /// <summary>
    /// Gets the base volume used by block interaction sounds.
    /// </summary>
    public float Volume { get; init; } = 1;
    /// <summary>
    /// Gets the base pitch used by block interaction sounds.
    /// </summary>
    public float Pitch { get; init; } = 1;

    /// <summary>The default block sounds, like vanilla's stone sound type.</summary>
    public static BlockSoundProperties Stone { get; } = new()
    {
        Break = new SoundEvent { ResourceLocation = "minecraft:block.stone.break" },
        Step = new SoundEvent { ResourceLocation = "minecraft:block.stone.step" },
        Place = new SoundEvent { ResourceLocation = "minecraft:block.stone.place" },
        Hit = new SoundEvent { ResourceLocation = "minecraft:block.stone.hit" },
        Fall = new SoundEvent { ResourceLocation = "minecraft:block.stone.fall" }
    };
}
