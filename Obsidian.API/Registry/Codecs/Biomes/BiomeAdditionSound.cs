namespace Obsidian.API.Registry.Codecs.Biomes;

public sealed record class BiomeAdditionSound
{
    public required string Sound { get; set; }

    public required double TickChance { get; set; }
}

public sealed record class BiomeMoodSound
{
    public required string Sound { get; set; }

    public required double Offset { get; set; }

    public required int TickDelay { get; set; }
    public required int BlockSearchExtent { get; set; }
}
