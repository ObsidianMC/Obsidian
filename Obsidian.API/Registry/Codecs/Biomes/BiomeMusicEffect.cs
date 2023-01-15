namespace Obsidian.API.Registry.Codecs.Biomes;

public sealed record class BiomeMusicEffect
{
    public required bool ReplaceCurrentMusic { get; set; }

    public required int MaxDelay { get; set; }

    public required string Sound { get; set; }

    public required int MinDelay { get; set; }
}
