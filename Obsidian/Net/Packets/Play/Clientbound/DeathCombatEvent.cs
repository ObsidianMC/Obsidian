using Obsidian.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class DeathCombatEvent :IClientboundPacket
{
    [Field(0), VarLength]
    public int PlayerId { get; init; }

    [Field(1)]
    public int EntityId { get; init; }

    [Field(2)]
    public ChatMessage Message { get; init; }

    public int Id => 0x35;
}
