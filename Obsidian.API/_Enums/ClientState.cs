namespace Obsidian.API;
/// <summary>The protocol state of a client connection, which decides which packets it can send and receive.</summary>
public enum ClientState
{
    /// <summary>The client has just connected and is choosing what it wants to do.</summary>
    Handshaking = 0,
    /// <summary>The client is asking for the server list status.</summary>
    Status = 1,
    /// <summary>The client is logging in.</summary>
    Login = 2,
    /// <summary>The client has logged in and is receiving registries and other setup data.</summary>
    Configuration = 3,
    /// <summary>The client is in the world.</summary>
    Play = 4,
    /// <summary>The connection has closed.</summary>
    Closed = -1,
}
