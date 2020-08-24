using System.Threading.Tasks;
using Obsidian.Util;

namespace Obsidian.Net.Packets.Handshaking
{
    public class Handshake : Packet
    {
        public ProtocolVersion Version;

        public string ServerAddress;

        public ushort ServerPort;

        public ClientState NextState;

        public Handshake(byte[] data) : base(0x00, data)
        {
        }

        public Handshake() : base(0x00, null)
        {
        }

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WriteVarIntAsync((int)this.Version);
            //TODO: add string length check
            await stream.WriteStringAsync(this.ServerAddress);
            await stream.WriteUnsignedShortAsync(this.ServerPort);
            await stream.WriteVarIntAsync((int)this.NextState);
        }

        protected override async Task PopulateAsync(MinecraftStream stream)
        {
            this.Version = (ProtocolVersion)await stream.ReadVarIntAsync();
            this.ServerAddress = await stream.ReadStringAsync();
            this.ServerPort = await stream.ReadUnsignedShortAsync();
            this.NextState = (ClientState)await stream.ReadVarIntAsync();
        }
    }
}