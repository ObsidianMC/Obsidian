using Obsidian.Net;
using System.Threading.Tasks;

namespace Obsidian.Entities
{
    public class ChestedHorse : AbstractHorse
    {
        public bool HasChest { get; set; }

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await base.WriteAsync(stream);

            await stream.WriteEntityMetdata(18, EntityMetadataType.Boolean, this.HasChest);
        }
    }

    public class Llama : ChestedHorse
    {
        public int Strength { get; set; }

        public int CarpetColor { get; set; }

        public LlamaVariant Variant { get; set; }

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await base.WriteAsync(stream);

            await stream.WriteEntityMetdata(19, EntityMetadataType.VarInt, this.Strength);
            await stream.WriteEntityMetdata(20, EntityMetadataType.VarInt, this.CarpetColor);
            await stream.WriteEntityMetdata(21, EntityMetadataType.VarInt, this.Variant);
        }
    }

    public enum LlamaVariant : int
    {
        CreamyLlama,

        WhiteLlama, 

        BrownLlama,

        GrayLlama
    }

    public class Donkey : ChestedHorse { }
}
