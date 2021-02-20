using System;
using System.Threading.Tasks;

using Obsidian.Entities;
using Obsidian.Net.Actions.BossBar;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class BossBar : IPacket
    {
        [Field(0)]
        public Guid UUID { get; private set; }

        [Field(1)]
        public BossBarAction Action { get; private set; }

        public int Id => 0x0C;

        public BossBar(Guid uuid, BossBarAction action)
        {
            this.UUID = uuid;
            this.Action = action ?? throw new ArgumentNullException(nameof(action));
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}