using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Handshaking
{
    public class Handshake : IPacket
    {
        [Field(0, Type = DataType.VarInt)]
        public ProtocolVersion Version;

        [Field(1)]
        public string ServerAddress;

        [Field(2)]
        public ushort ServerPort;

        [Field(3, Type = DataType.VarInt)]
        public ClientState NextState;

        public int Id => 0x00;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}