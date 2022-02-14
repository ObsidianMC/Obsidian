namespace Obsidian.API.Registry.Codecs.Biomes;

public class BiomeCodec : ICodec
{
    public string Name { get; set; }

    public int Id { get; set; }

    public BiomeElement Element { get; set; }
}
