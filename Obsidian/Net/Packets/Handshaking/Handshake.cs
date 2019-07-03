using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class Handshake : Packet
    {
        [Variable(VariableType.VarInt)]
        public ProtocolVersion Version;

        [Variable(VariableType.String)]
        public string ServerAddress;

        [Variable(VariableType.UnsignedShort)]
        public ushort ServerPort;

        [Variable(VariableType.VarInt)]
        public ClientState NextState;

        public Handshake(byte[] data) : base(0x00, data)
        {
    
        }

        public Handshake() : base(0x00, null){}

        public override async Task DeserializeAsync()
        {
            using(var stream = new MinecraftStream(this.PacketData))
            {
                this.Version = (ProtocolVersion)await stream.ReadVarIntAsync();
                this.ServerAddress = await stream.ReadStringAsync();
                this.ServerPort = await stream.ReadUnsignedShortAsync();
                this.NextState = (ClientState)await stream.ReadVarIntAsync();
            }
        }

    }
}