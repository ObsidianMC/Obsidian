using Obsidian.Boss;
using Obsidian.Util;
using System;

namespace Obsidian.Net.Packets
{
    public class BossBar : Packet
    {
        public BossBar(Guid uuid, BossBarAction action) : base(0x0C)
        {
            this.UUID = uuid;
            this.Action = action ?? throw new ArgumentNullException(nameof(action));
        }

        [Variable]
        public Guid UUID { get; private set; }

        [Variable]
        public BossBarAction Action { get; private set; }
    }
}
