using Microsoft.Extensions.Logging;
using Obsidian.API.Networking;
using System.ComponentModel;

namespace Obsidian.API;
/// <summary>
/// A connection to a Minecraft client, through the login, configuration and play states.
/// </summary>
public interface IClient : IDisposable
{
    /// <summary>
    /// Gets the entity id of the client's player, which the packet broadcaster addresses the client by.
    /// </summary>
    public int Id { get; }
    /// <summary>
    /// The client brand. This is the name that the client used to identify itself (Fabric, Forge, Quilt, etc.)
    /// </summary>
    public string? Brand { get; set; }
    /// <summary>
    /// Gets the client's IP address, or <see langword="null"/> if it isn't known.
    /// </summary>
    public string? Ip { get; }

    /// <summary>
    /// Gets a value indicating whether the client's socket is still connected.
    /// </summary>
    public bool Connected { get; }

    /// <summary>
    /// Gets or sets the id of the last keep-alive sent that the client hasn't answered yet, or <see langword="null"/>.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public long? LastKeepAliveId { get; set; }

    /// <summary>
    /// The server. During a plugin's calls, like its event handlers, commands and behaviours, it's the plugin's own
    /// <see cref="IServer"/>, whose additions are the plugin's
    /// (see <see cref="Events.BaseMinecraftEventArgs.Server"/>).
    /// </summary>
    public IServer Server { get; }

    /// <summary>
    /// Gets the protocol state the connection is in.
    /// </summary>
    public ClientState State { get; }

    /// <summary>
    /// Gets or sets the chat signing key the client sent, or <see langword="null"/> if it sent none.
    /// </summary>
    public SignatureData? SignatureData { get; set; }

    /// <summary>
    /// Gets or sets the client's round-trip time in milliseconds, measured with keep-alives.
    /// </summary>
    public int Ping { get; set; }

    /// <summary>
    /// The verify token sent in the encryption request; empty until the request is sent.
    /// </summary>
    public ReadOnlyMemory<byte> RandomToken { get; }

    /// <summary>
    /// The player that the client is logged in as.
    /// </summary>
    public IPlayer? Player { get; }

    /// <summary>
    /// Gets the logger for the connection.
    /// </summary>
    public ILogger Logger { get; }

    /// <summary>
    /// Sends a packet without packet handlers: immediately, or right after the packets queued before it that are still
    /// on their way.
    /// </summary>
    /// <returns>Whether the packet was sent or is on its way.</returns>
    public bool SendPacket(IClientboundPacket packet);

    /// <summary>
    /// Decrypts and keeps the shared secret the client sent in its encryption response, which enables encryption, and
    /// decrypts the verify token it echoed.
    /// </summary>
    /// <param name="sharedKey">The encrypted shared secret.</param>
    /// <param name="verifyToken">The encrypted verify token.</param>
    /// <returns>The decrypted verify token, to compare with <see cref="RandomToken"/>.</returns>
    public ReadOnlySpan<byte> SetSharedKeyAndDecodeVerifyToken(ReadOnlySpan<byte> sharedKey, ReadOnlySpan<byte> verifyToken);

    /// <summary>
    /// Sends <paramref name="reason"/> to the client and closes the connection once the packets queued before it are
    /// sent, or after a timeout. A player who joined leaves the server.
    /// </summary>
    /// <param name="reason">The message the client shows.</param>
    public ValueTask DisconnectAsync(ChatMessage reason);

    /// <summary>
    /// Queues a packet in the client's outgoing pipeline, where packet handlers see it in order before it's sent. The
    /// call never waits for the handlers.
    /// </summary>
    public ValueTask QueuePacketAsync(IClientboundPacket packet);

    /// <summary>
    /// Moves the connection to <paramref name="state"/>, which decides how the following packets are read.
    /// </summary>
    /// <param name="state">The new state.</param>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void SetState(ClientState state);

    /// <summary>
    /// Checks with Mojang's session server that the player owns their account, and disconnects the client if not.
    /// </summary>
    /// <returns><see langword="true"/> if the profile was verified; otherwise, <see langword="false"/>.</returns>
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
