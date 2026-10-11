using Microsoft.Extensions.Logging;
using Obsidian.API.Networking;
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

    /// <summary>
    /// The verify token sent in the encryption request; empty until the request is sent.
    /// </summary>
    public ReadOnlyMemory<byte> RandomToken { get; }

    /// <summary>
    /// The player that the client is logged in as.
    /// </summary>
    public IPlayer? Player { get; }

    public ILogger Logger { get; }

    public bool SendPacket(IClientboundPacket packet);

    public ReadOnlySpan<byte> SetSharedKeyAndDecodeVerifyToken(ReadOnlySpan<byte> sharedKey, ReadOnlySpan<byte> verifyToken);

    public ValueTask DisconnectAsync(ChatMessage reason);
    public ValueTask QueuePacketAsync(IClientboundPacket packet);

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void SetState(ClientState state);

    public ValueTask<bool> VerifyProfileAsync();

    /// <summary>
    /// Whether the connection may receive payloads on <paramref name="channel"/> in its current phase: a vanilla
    /// channel, one negotiated with a NeoForge client, or one the client registered with <c>minecraft:register</c> or
    /// <c>c:register</c>.
    /// </summary>
    public bool HasChannel(string channel);

    /// <summary>
    /// Sends <paramref name="payload"/> if the connection may receive its channel (see <see cref="HasChannel"/>).
    /// </summary>
    /// <returns>Whether the payload was queued.</returns>
    public ValueTask<bool> SendPayloadAsync<TPayload>(TPayload payload) where TPayload : ICustomPayload<TPayload>;
}
