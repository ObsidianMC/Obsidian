using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace Obsidian.API;
public interface IClient : IDisposable
{
    public int Id { get; }
    /// <summary>
    /// The client brand. This is the name that the client used to identify itself (Fabric, Forge, Quilt, etc.)
    /// </summary>
    public string? Brand { get; set; }
    public string? Ip { get; }

    public bool Connected { get; }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public long? LastKeepAliveId { get; set; }

    public IServer Server { get; }

    public ClientState State { get; }

    public SignatureData? SignatureData { get; set; }

    public int Ping { get; set; }

    public byte[]? RandomToken { get; }

    /// <summary>
    /// The player that the client is logged in as.
    /// </summary>
    public IPlayer? Player { get; }

    public ILogger Logger { get; }

    public bool SendPacket(IClientboundPacket packet);

    public ReadOnlySpan<byte> SetSharedKeyAndDecodeVerifyToken(byte[] sharedKey, byte[] verifyToken);

    public ValueTask DisconnectAsync(ChatMessage reason);
    public ValueTask QueuePacketAsync(IClientboundPacket packet);

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void SetState(ClientState state);

    public ValueTask<bool> VerifyProfileAsync();
}
