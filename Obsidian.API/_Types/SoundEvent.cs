namespace Obsidian.API;
public readonly struct SoundEvent
{
    public required string ResourceLocation { get; init; }
    public float? FixedRange { get; init; }
}
