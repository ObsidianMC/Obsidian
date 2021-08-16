using Obsidian.API;
using Obsidian.Net;
using System.Threading.Tasks;

namespace Obsidian.Entities
{
    public class FallingBlock : Entity
    {
        public Vector SpawnPosition { get; private set; }

        public Material BlockMaterial { get; set; }

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await base.WriteAsync(stream);

            await stream.WriteEntityMetdata(7, EntityMetadataType.VarInt, BlockMaterial);
        }

        public override void Write(MinecraftStream stream)
        {
            base.Write(stream);

            stream.WriteEntityMetadataType(7, EntityMetadataType.VarInt);
        }
    }
}
