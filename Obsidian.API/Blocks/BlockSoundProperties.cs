namespace Obsidian.API.Blocks;

/// <summary>
/// A block's break, step, place, hit and fall events, with the base volume and pitch used by block interactions.
/// Declare the same sounds in ConfigureRegistry on both sides. Reloading replaces them; unloading uses stone.
/// </summary>
public sealed record BlockSoundProperties
{
    public required SoundEvent Break { get; init; }
    public required SoundEvent Step { get; init; }
    public required SoundEvent Place { get; init; }
    public required SoundEvent Hit { get; init; }
    public required SoundEvent Fall { get; init; }
    public float Volume { get; init; } = 1;
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
