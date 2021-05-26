using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    [ServerOnly]
    public partial class ClientSettings : IPacket
    {
        [Field(0)]
        public string Locale { get; private set; }

        [Field(1)]
        public sbyte ViewDistance { get; private set; }

        [Field(2)]
        public int ChatMode { get; private set; }

        [Field(3)]
        public bool ChatColors { get; private set; }

        [Field(4)]
        public byte SkinParts { get; private set; } // skin parts that are displayed. might not be necessary to decode?

        [Field(5)]
        public int MainHand { get; private set; }

        public int Id => 0x05;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.Locale = await stream.ReadStringAsync();
            this.ViewDistance = await stream.ReadByteAsync();
            this.ChatMode = await stream.ReadVarIntAsync();
            this.ChatColors = await stream.ReadBooleanAsync();
            this.SkinParts = await stream.ReadUnsignedByteAsync();
            this.MainHand = await stream.ReadVarIntAsync();
        }

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}