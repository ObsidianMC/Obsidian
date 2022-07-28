namespace Obsidian.API._Types.Config;

public class RconConfig
{
    /// <summary>
    /// Password to access the RCON.
    /// </summary>
    public string Password { get; set; }

    /// <summary>
    /// Port on which RCON server listens.
    /// </summary>
    public ushort Port { get; set; }

    /// <summary>
    /// Whether the RCON commands should be sent to currently online Operators.
    /// </summary>
    public bool BroadcastToOps { get; set; }

    /// <summary>
    /// Whether the server will require the encryption before authenticating
    /// </summary>
    public bool RequireEncryption { get; set; }
}
