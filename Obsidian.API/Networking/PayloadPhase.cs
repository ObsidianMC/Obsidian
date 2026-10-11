namespace Obsidian.API.Networking;

/// <summary>
/// The connection phase a payload channel is used in.
/// </summary>
public enum PayloadPhase
{
    /// <summary>
    /// While the client is being configured, before it joins the world.
    /// </summary>
    Configuration,

    /// <summary>
    /// While the player is in the world.
    /// </summary>
    Play,

    /// <summary>
    /// Both phases, like NeoForge's "common" payload registrations.
    /// </summary>
    Common
}
