namespace Obsidian.API.Registry.Codecs.Biomes;

public sealed record class BiomeMusicEffect
{
    public bool ReplaceCurrentMusic { get; set; }

    public int MaxDelay { get; set; }

    public string Sound { get; set; }

    public int MinDelay { get; set; }
}
