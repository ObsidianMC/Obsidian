using Obsidian.BossBar;
using Obsidian.Util;
using System;
using System.Threading.Tasks;

namespace Obsidian.Packets.Play
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

        public override async Task<byte[]> ToArrayAsync()
        {
            //NOTE: Uncomment if set should be made public
            //if (Action == null) throw new Exception("Action is null!");

            using (var stream = new MinecraftStream())
            {
                await stream.WriteUuidAsync(this.UUID);
                await stream.WriteVarIntAsync(this.Action.Action);
                await stream.WriteAsync(await this.Action.ToArrayAsync());
                return stream.ToArray();
            }
        }

        protected override Task PopulateAsync() => throw new NotImplementedException();
    }
}
