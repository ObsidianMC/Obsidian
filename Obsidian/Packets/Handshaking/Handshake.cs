using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets.Handshaking
{
    public class Handshake
    {
        public ProtocolVersion Version;
        
        public string ServerAddress;

        public ushort ServerPort;

        public PacketState NextState;

        public Handshake()
        {
        }

        public static async Task<Handshake> FromArrayAsync(byte[] data)
        {
            using(MemoryStream stream = new MemoryStream(data))
            {
                return new Handshake() {
                    Version = (ProtocolVersion)await stream.ReadVarIntAsync(),
                    ServerAddress = await stream.ReadStringAsync(),
                    ServerPort = await stream.ReadUnsignedShortAsync(),
                    NextState = (PacketState)await stream.ReadVarIntAsync(),
                };
            }
        }

        public async Task<byte[]> ToArrayAsync()
        {
            using (MemoryStream stream = new MemoryStream())
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