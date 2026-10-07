using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class SelectTradePacket
{
    [Field(0), VarLength]
    public int Item { get; private set; }
    public override void Populate(INetStreamReader reader) => Item = reader.ReadVarInt();
    public override async ValueTask HandleAsync(IServer server, IPlayer player)
    {
        if (player.OpenedContainer is MerchantContainer merchant) await merchant.SelectAsync(Item);
    }
}
