using Obsidian.Nbt;

namespace Obsidian.Utilities.Registry.Codecs.Biomes;

public class BiomeCodec
{
    public string Name { get; set; }

    public int Id { get; set; }

    public BiomeElement Element { get; set; }

    public void Write(NbtList list)
    {
        var compound = new NbtCompound
            {
                new NbtTag<string>("name", this.Name),
                new NbtTag<int>("id", this.Id)
            };

        this.Element.Write(compound);

        list.Add(compound);
    }
}
