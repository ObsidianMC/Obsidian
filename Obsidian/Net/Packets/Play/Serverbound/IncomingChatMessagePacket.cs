using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class IncomingChatMessagePacket : IServerboundPacket
{
    [Field(0)]
    public string Message { get; private set; }

    [Field(1)]
    public long Salt { get; private set; }

    [Field(2), VarLength]
    public int SignatureLength { get; private set; }

    [Field(3)]
    public byte[] Signature { get; private set; }

    [Field(4)]
    public bool SignedPreview { get; set; }

    public string Format { get; private set; }

    public int Id => 0x04;

    public void Populate(MinecraftStream stream)
    {
        Message = stream.ReadString();
        Format = "<{0}> {1}";
    }

    //TODO verify message signature https://wiki.vg/images/f/f4/MinecraftChat.drawio4.png
    public async ValueTask HandleAsync(Server server, Player player)
    {
        await server.HandleIncomingMessageAsync(Message, Format, player.client);
    }
}
