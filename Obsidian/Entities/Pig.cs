using Obsidian.Net;
using System.Threading.Tasks;

namespace Obsidian.Entities
{
    public class Pig : Animal
    {
        public bool HasSaddle { get; set; }

        public int TotalTimeBoost { get; set; }

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await base.WriteAsync(stream);

            await stream.WriteEntityMetdata(16, EntityMetadataType.Boolean, this.HasSaddle);
            await stream.WriteEntityMetdata(17, EntityMetadataType.VarInt, this.TotalTimeBoost);
        }
    }
}
