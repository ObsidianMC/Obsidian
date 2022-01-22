using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class TabCompleteRequest : IServerboundPacket
{
    [Field(0), VarLength]
    public int TransactionId { get; private set; }

    [Field(1)]
    public string Text { get; private set; }

    public int Id => 0x06;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
