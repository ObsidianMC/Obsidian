using Obsidian.Boss;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class BossBar : Packet
    {
        public BossBar(Guid uuid, BossBarAction action) : base(0x0C)
        {
            this.UUID = uuid;
            this.Action = action ?? throw new ArgumentNullException(nameof(action));
        }

        public Guid UUID { get; private set; }
        public BossBarAction Action { get; private set; }

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WriteUuidAsync(this.UUID);
            await stream.WriteVarIntAsync(this.Action.Action);
            await stream.WriteAsync(await this.Action.ToArrayAsync());
        }

        protected override Task PopulateAsync(MinecraftStream stream) => throw new NotImplementedException();
    }
}