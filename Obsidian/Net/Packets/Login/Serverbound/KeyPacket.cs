using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Net.Packets.Login.Clientbound;
using Obsidian.Serialization.Attributes;
using Obsidian.Utilities.Mojang;
using System.Net.Sockets;

namespace Obsidian.Net.Packets.Login.Serverbound;

public partial class KeyPacket
{
    [Field(0)]
    public byte[] SharedSecret { get; private set; } = default!;

    [Field(1)]
    public byte[] VerifyToken { get; private set; } = default!;

    public override void Populate(INetStreamReader reader)
    {
        this.SharedSecret = reader.ReadByteArray();
        this.VerifyToken = reader.ReadByteArray();
    }

    public async override ValueTask HandleAsync(IClient client)
    {
        var decryptedToken = client.SetSharedKeyAndDecodeVerifyToken(this.SharedSecret, this.VerifyToken);

        if (!decryptedToken.SequenceEqual(client.RandomToken!))
        {
            await client.DisconnectAsync("Invalid token...");
            return;
        }

        await client.VerifyProfileAsync();
    }
}
