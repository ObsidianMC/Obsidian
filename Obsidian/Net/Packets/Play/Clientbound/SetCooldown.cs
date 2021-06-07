using Obsidian.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class SetCooldown : IClientboundPacket
    {
        [Field(0), VarLength]
        public int ItemId;

        [Field(1), VarLength]
        public int CooldownTicks;

        public int Id => 0x16;
    }
}
