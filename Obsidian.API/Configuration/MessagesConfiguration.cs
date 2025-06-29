namespace Obsidian.API.Configuration;
public sealed record class MessagesConfiguration
{
    public string Join { get; set; } = "{0} joined the game";

    public string Leave { get; set; } = "{0} left the game";

    public string NotWhitelisted { get; set; } = "You are not whitelisted on this server!";

    public string ServerFull { get; set; } = "The server is full!";

    public string OutdatedClient { get; set; } = "Outdated client! Please use {0}";
    public string OutdatedServer { get; set; } = "Outdated server! I'm still on {0}";
}

