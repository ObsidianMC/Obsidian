namespace Obsidian.API;
public readonly record struct SoundEvent
{
    public required string ResourceLocation { get; init; }
    public float? FixedRange { get; init; }
}
