using Obsidian.Nbt.Tags;

namespace Obsidian.Utilities.Registry.Codecs.Biomes
{
    public class BiomeCodec
    {
        public string Name { get; set; }

        public int Id { get; set; }

        public BiomeElement Element { get; set; }

        public void Write(NbtList list)
        {
            var compound = new NbtCompound
            {
                new NbtString("name", this.Name),
                new NbtInt("id", this.Id)
            };

            this.Element.Write(compound);

            list.Add(compound);
        }
    }
}
