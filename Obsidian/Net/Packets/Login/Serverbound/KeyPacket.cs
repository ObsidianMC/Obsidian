using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login.Serverbound;

public partial class KeyPacket
{
    [Field(0)]
    public ReadOnlyMemory<byte> SharedSecret { get; private set; }

    [Field(1)]
    public ReadOnlyMemory<byte> VerifyToken { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.SharedSecret = reader.ReadByteArray();
        this.VerifyToken = reader.ReadByteArray();
    }

    public async override ValueTask HandleAsync(IClient client)
    {
        var decryptedToken = client.SetSharedKeyAndDecodeVerifyToken(this.SharedSecret.Span, this.VerifyToken.Span);

        if (!decryptedToken.SequenceEqual(client.RandomToken.Span))
        {
            await client.DisconnectAsync("Invalid token...");
            return;
        }

        await client.VerifyProfileAsync();
    }
}
