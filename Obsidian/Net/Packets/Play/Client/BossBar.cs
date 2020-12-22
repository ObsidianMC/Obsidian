using Obsidian.Boss;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public partial class BossBar : IPacket
    {
        [Field(0)]
        public Guid UUID { get; private set; }

        [Field(1)]
        public BossBarAction Action { get; private set; }

        public int Id => 0x0C;

        public BossBar() { }
        public BossBar(Guid uuid, BossBarAction action)
        {
            this.UUID = uuid;
            this.Action = action ?? throw new ArgumentNullException(nameof(action));
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}