namespace Obsidian.API.Registry.Codecs.Biomes;

public sealed record class BiomeMusicEffect
{
    public required bool ReplaceCurrentMusic { get; set; }

    public required int MaxDelay { get; set; }

    public required string Sound { get; set; }

    public required int MinDelay { get; set; }
}

public sealed record class BiomeMusicEffectData
{
    public required BiomeMusicEffect Data { get; set; }

    public int Weight { get; set; }
}
