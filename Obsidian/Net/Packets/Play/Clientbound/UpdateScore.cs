using Obsidian.Serialization.Attributes;
using Obsidian.Utilities;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class UpdateScore : IClientboundPacket
    {
        /// <summary>
        /// The entity whose score this is. For players, this is their username; for other entities, it is their UUID.
        /// </summary>
        [Field(0)]
        public string EntityName { get; set; }

        /// <summary>
        /// 0 to create/update an item. 1 to remove an item.
        /// </summary>
        [Field(1)]
        public byte Action { get; set; }

        /// <summary>
        /// The name of the objective the score belongs to.
        /// </summary>
        [Field(3)]
        public string ObjectiveName { get; set; }

        /// <summary>
        /// The score to be displayed next to the entry. Only sent when Action does not equal 1.
        /// </summary>
        [Field(4), VarLength]
        public int Value { get; set; }

        public int Id => 0x4D;

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
    }
}
