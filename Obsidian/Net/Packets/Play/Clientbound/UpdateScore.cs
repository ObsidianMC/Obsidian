using Obsidian.Entities;
using Obsidian.Util.Extensions;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public class UpdateScore : IPacket
    {
        /// <summary>
        /// The entity whose score this is. For players, this is their username; for other entities, it is their UUID.
        /// </summary>
        public string EntityName { get; set; }

        /// <summary>
        /// 0 to create/update an item. 1 to remove an item.
        /// </summary>
        public sbyte Action { get; set; }

        /// <summary>
        /// The name of the objective the score belongs to.
        /// </summary>
        public string ObjectiveName { get; set; }

        /// <summary>
        /// The score to be displayed next to the entry. Only sent when Action does not equal 1.
        /// </summary>
        public int Value { get; set; }

        public int Id => 0x4D;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public void Serialize(MinecraftStream stream)
        {
            using var packetStream = new MinecraftStream();
            packetStream.WriteString(this.EntityName, 40);
            packetStream.WriteByte(this.Action);

            packetStream.WriteString(this.ObjectiveName, 16);

            if (this.Action == 0)
                packetStream.WriteVarInt(this.Value);

            stream.Lock.Wait();

            stream.WriteVarInt(this.Id.GetVarIntLength() + (int)packetStream.Length);
            stream.WriteVarInt(this.Id);

            packetStream.Position = 0;
            packetStream.CopyTo(stream);

            stream.Lock.Release();
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;
    }
}
