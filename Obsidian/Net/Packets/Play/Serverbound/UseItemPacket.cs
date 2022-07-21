using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;
public partial class UseItemPacket : IServerboundPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public Hand Hand { get; private set; }


    [Field(1), VarLength]
    public int Sequence { get; private set; }

    public int Id => 0x31;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
