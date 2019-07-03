using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class Handshake : Packet
    {
        [Variable(VariableType.VarInt, 0)]
        public ProtocolVersion Version { get; set; }

        [Variable(VariableType.String, 1)]
        public string ServerAddress { get; set; }

        [Variable(VariableType.UnsignedShort, 2)]
        public ushort ServerPort { get; set; }

        [Variable(VariableType.VarInt, 3)]
        public ClientState NextState { get; set; }

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