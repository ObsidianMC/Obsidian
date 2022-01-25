namespace Obsidian.API.Registry.Codecs.Biomes;

public sealed class BiomeAdditionSound
{
    public string Sound { get; set; }

    public double TickChance { get; set; }
}

public sealed class BiomeMoodSound
{
    public string Sound { get; set; }

    public double Offset { get; set; }

    public int TickDelay { get; set; }
    public int BlockSearchExtent { get; set; }
}
