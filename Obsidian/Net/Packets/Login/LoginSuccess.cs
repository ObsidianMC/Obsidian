using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Login
{
    public class LoginSuccess : IPacket
    {
        [Field(0)]
        public Guid UUID { get; set; }

        [Field(1)]
        public string Username { get; set; }

        public int Id => 0x02;

        public LoginSuccess(Guid uuid, string username)
        {
            this.Username = username;
            this.UUID = uuid;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}