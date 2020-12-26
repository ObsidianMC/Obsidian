using Obsidian.Net;
using System.Threading.Tasks;

namespace Obsidian.Entities
{
    public class Horse : AbstractHorse
    {
        public int Variant { get; set; }

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await base.WriteAsync(stream);

            await stream.WriteEntityMetdata(18, EntityMetadataType.VarInt, this.Variant);
        }

        public override void Write(MinecraftStream stream)
        {
            base.Write(stream);

            stream.WriteEntityMetadataType(18, EntityMetadataType.VarInt);
            stream.WriteVarInt(Variant);
        }
    }
}
