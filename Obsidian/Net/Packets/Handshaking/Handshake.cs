using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Handshaking
{
    public partial class Handshake : IClientboundPacket, IServerboundPacket
    {
        [Field(0), ActualType(typeof(int)), VarLength]
        public ProtocolVersion Version { get; private set; }

        [Field(1)]
        public string ServerAddress { get; private set; }

        [Field(2)]
        public ushort ServerPort { get; private set; }

        [Field(3), ActualType(typeof(int)), VarLength]
        public ClientState NextState { get; private set; }

        public int Id => 0x00;

        public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
    }
}
