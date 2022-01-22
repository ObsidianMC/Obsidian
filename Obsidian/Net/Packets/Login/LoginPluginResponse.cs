using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Login;

public partial class LoginPluginResponse : IServerboundPacket
{
    [Field(0), VarLength]
    public int MessageId { get; set; }

    [Field(1)]
    public bool Successful { get; set; }

    [Field(2)]
    public byte[] Data { get; set; }

    public int Id => 0x04;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
