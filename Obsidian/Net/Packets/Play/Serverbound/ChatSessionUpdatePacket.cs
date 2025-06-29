using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;
public sealed partial class ChatSessionUpdatePacket
{
    [Field(0)]
    public Guid SessionId { get; private set; }

    [Field(1)]
    public SignatureData SignatureData { get; private set; }

    public override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        player.Client.SignatureData = this.SignatureData;

        return default;
    }

    public override void Populate(INetStreamReader reader)
    {
        this.SessionId = reader.ReadGuid();
        this.SignatureData = new()
        {
            ExpirationTime = reader.ReadDateTimeOffset(),
            PublicKey = reader.ReadUInt8Array(),
            Signature = reader.ReadUInt8Array()
        };
    }
}
