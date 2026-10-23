using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;
public partial class UseItemPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public InteractionHand Hand { get; private set; }


    [Field(1), VarLength]
    public int Sequence { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Hand = reader.ReadVarInt<InteractionHand>();
        this.Sequence = reader.ReadVarInt();
        reader.ReadSingle();
        reader.ReadSingle();
    }

    public override ValueTask HandleAsync(IServer server, IClientPlayer player) => player.UseItemAsync(this.Hand);
}
