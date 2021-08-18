using Obsidian.API;
using Obsidian.Net;
using System.Threading.Tasks;

namespace Obsidian.Entities
{
    public class FallingBlock : Entity
    {
        public VectorF SpawnPosition { get; private set; }

        public Velocity Velocity { get; set; }

        public Material BlockMaterial { get; set; }

        public FallingBlock() : base()
        {
            SpawnPosition = Position;
        }

        public async override Task TickAsync()
        {
            
        }

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await base.WriteAsync(stream);

            //await stream.WriteEntityMetdata(7, EntityMetadataType.VarInt, SpawnPosition);
        }

        public override void Write(MinecraftStream stream)
        {
            base.Write(stream);

/*            stream.WriteEntityMetadataType(7, EntityMetadataType.VarInt);
            stream.WriteVarInt(SpawnPosition);*/
        }
    }
}
