using Obsidian.API.Networking;

namespace Obsidian.API.Client;

/// <summary>
/// Handles a payload the server sent on a mod's channel. It runs on the connection's packet thread, so hand work for
/// the game over to it rather than blocking.
/// </summary>
/// <param name="payload">The payload.</param>
/// <param name="connection">The connection it arrived on, to answer on.</param>
public delegate void ClientPayloadHandler<TPayload>(TPayload payload, IClientConnection connection)
    where TPayload : ICustomPayload<TPayload>;

/// <summary>
/// The client's connection to a server, which a mod sends its payloads on.
/// </summary>
public interface IClientConnection
{
    /// <summary>
    /// The client's read-only world view while playing, or null during login, configuration and disconnection.
    /// Obtain it again after changing servers; see <see cref="IClientWorld"/> for thread rules.
    /// </summary>
    public IClientWorld? World { get; }

    /// <summary>
    /// The phase the connection is in: <see cref="PayloadPhase.Configuration"/> or <see cref="PayloadPhase.Play"/>,
    /// or null while the client isn't connected (or is still logging in).
    /// </summary>
    public PayloadPhase? Phase { get; }

    /// <summary>
    /// Sends a payload to the server, if the server agreed on its channel (in NeoForge's handshake, or by registering
    /// it) and the mod declared the channel with <see cref="Plugins.IPluginRegistry.MapPayload{TPayload}"/> for the
    /// current phase.
    /// </summary>
    /// <returns>Whether the payload was sent.</returns>
    public bool SendPayload<TPayload>(TPayload payload) where TPayload : ICustomPayload<TPayload>;
}
