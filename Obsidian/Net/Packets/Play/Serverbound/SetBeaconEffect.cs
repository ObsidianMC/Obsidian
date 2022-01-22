using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class SetBeaconEffect : IServerboundPacket
{
    [Field(0), VarLength]
    public int PrimaryEffect { get; set; }
    
    [Field(1), VarLength]
    public int SecondaryEffect { get; set; }

    public int Id => 0x24;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
