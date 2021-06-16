using Obsidian.Net.Actions.BossBar;
using Obsidian.Serialization.Attributes;
using System;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class BossBar : IClientboundPacket
    {
        [Field(0)]
        public Guid UUID { get; }

        [Field(1)]
        public BossBarAction Action { get; }

        public int Id => 0x0C;

        public BossBar(Guid uuid, BossBarAction action)
        {
            UUID = uuid;
            Action = action ?? throw new ArgumentNullException(nameof(action));
        }
    }
}
