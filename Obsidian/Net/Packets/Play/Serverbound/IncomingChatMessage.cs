using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    [ServerOnly]
    public partial class IncomingChatMessage : IServerboundPacket
    {
        [Field(0)]
        public string Message { get; private set; }
        public string Format { get; private set; }

        public int Id => 0x03;

        public void Populate(byte[] data)
        {
            using var stream = new MinecraftStream(data);
            Populate(stream);
        }

        public void Populate(MinecraftStream stream)
        {
            Message = stream.ReadString();
            Format = "<{0}> {1}";
        }

        public Task HandleAsync(Server server, Player player) =>
            server.ParseMessageAsync(this.Message, this.Format, player.client);
    }
}