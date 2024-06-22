namespace Obsidian.API.Configuration;
public sealed class WhitelistConfiguration
{
    public List<WhitelistedPlayer> WhitelistedPlayers { get; set; } = [];

    public List<string> WhitelistedIps { get; set; } = [];
}
