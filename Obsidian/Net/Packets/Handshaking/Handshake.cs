using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Handshaking
{
    public partial class Handshake : IClientboundPacket, IServerboundPacket
    {
        [Field(0), ActualType(typeof(int)), VarLength]
        public ProtocolVersion Version;

        [Field(1)]
        public string ServerAddress;

        [Field(2)]
        public ushort ServerPort;

        [Field(3), ActualType(typeof(int)), VarLength]
        public ClientState NextState;

        public int Id => 0x00;

        public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
    }
}