namespace Obsidian.API.Configuration;
/// <summary>
/// Stores the player and IP address entries admitted by the whitelist.
/// </summary>
public sealed class WhitelistConfiguration
{
    /// <summary>
    /// Gets or sets the player identities admitted by the whitelist.
    /// </summary>
    public List<WhitelistedPlayer> WhitelistedPlayers { get; set; } = [];

    /// <summary>
    /// Gets or sets the IP addresses admitted by the whitelist.
    /// </summary>
    public List<string> WhitelistedIps { get; set; } = [];
}
