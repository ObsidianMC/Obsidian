using Obsidian.Net.Actions.BossBar;
using Obsidian.Serialization.Attributes;
using System;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class BossBar : IClientboundPacket
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
    }
}