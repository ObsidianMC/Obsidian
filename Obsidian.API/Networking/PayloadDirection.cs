namespace Obsidian.API.Networking;

/// <summary>
/// Which way a payload channel carries payloads. A NeoForge mod must declare the same direction for the channel, or
/// the connection is refused.
/// </summary>
public enum PayloadDirection
{
    /// <summary>
    /// Only from the server to the client.
    /// </summary>
    Clientbound,

    /// <summary>
    /// Only from the client to the server.
    /// </summary>
    Serverbound,

    /// <summary>
    /// Both ways.
    /// </summary>
    Bidirectional
}
