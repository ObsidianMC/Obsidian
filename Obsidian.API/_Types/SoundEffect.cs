namespace Obsidian.API;
public sealed class SoundEffect : ISoundEffect
{
    public required string SoundId { get; init; }
    public required SoundCategory SoundCategory { get; init; }

    public required float Volume { get; init; }
    public required float Pitch { get; init; }
    public required long Seed { get; init; }

    public float? FixedRange { get; init; }

    public string? SoundName { get; init; }
    public int? EntityId { get; init; }

    public SoundPosition? SoundPosition { get; init; }

    internal SoundEffect() { }
}
