using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Login
{
    [ClientOnly]
    public partial class LoginSuccess : ISerializablePacket
    {
        [Field(0)]
        public Guid UUID { get; }

        [Field(1)]
        public string Username { get; }

        public int Id => 0x02;

        public LoginSuccess(Guid uuid, string username)
        {
            Username = username;
            UUID = uuid;
        }

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}