using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.Packets.Handshaking
{
    public class Handshake : Packet
    {
        public ProtocolVersion Version;
        
        public string ServerAddress;

        public ushort ServerPort;

        public PacketState NextState;

        public Handshake(byte[] data) : base(0x00, data)
        {
    
        }

        public Handshake() : base(0x00, null){}

        protected override async Task PopulateAsync()
        {
            using(var stream = new MinecraftStream(this._packetData))
            {
                this.Version = (ProtocolVersion)await stream.ReadVarIntAsync();
                this.ServerAddress = await stream.ReadStringAsync();
                this.ServerPort = await stream.ReadUnsignedShortAsync();
                this.NextState = (PacketState)await stream.ReadVarIntAsync();
            }
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteVarIntAsync((int)this.Version);
                //TODO: add string length check
                await stream.WriteStringAsync(this.ServerAddress);
                await stream.WriteUnsignedShortAsync(this.ServerPort);
                await stream.WriteVarIntAsync((int)this.NextState);

                return stream.ToArray();
            }
        }

    }
}