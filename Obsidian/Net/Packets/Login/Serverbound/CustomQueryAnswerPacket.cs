using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login.Serverbound;
public partial class CustomQueryAnswerPacket
{
    [Field(0)]
    [VarLength]
    public int MessageId { get; private set; }

    [Field(1)]
    public bool Successful { get; private set; }

    [Field(2)]
    [Condition("Successful")]
    public byte[] Data { get; private set; } = default!;

    public override void Populate(INetStreamReader reader)
    {
        this.MessageId = reader.ReadVarInt();
        this.Successful = reader.ReadBoolean();

        //TODO we need a query registry.
        if(this.Successful)
        {
            throw new NotImplementedException();
        }
    }

    //TODO handle this maybe as an event?
    public override ValueTask HandleAsync(Server server, Player player)
    {

        return default;
    }
}
