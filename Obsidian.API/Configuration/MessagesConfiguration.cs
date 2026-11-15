namespace Obsidian.API.Configuration;
/// <summary>
/// Configures the text used for common join, leave and connection rejection messages.
/// </summary>
public sealed record class MessagesConfiguration
{
    /// <summary>
    /// Gets or sets the join message format, where <c>{0}</c> is the player's username.
    /// </summary>
    public string Join { get; set; } = "{0} joined the game";

    /// <summary>
    /// Gets or sets the leave message format, where <c>{0}</c> is the player's username.
    /// </summary>
    public string Leave { get; set; } = "{0} left the game";

    /// <summary>
    /// Gets or sets the rejection message for a player excluded by the whitelist.
    /// </summary>
    public string NotWhitelisted { get; set; } = "You are not whitelisted on this server!";

    /// <summary>
    /// Gets or sets the rejection message when the server has reached its player limit.
    /// </summary>
    public string ServerFull { get; set; } = "The server is full!";

    /// <summary>
    /// Gets or sets the message template for a client protocol older than the server's.
    /// </summary>
    public string OutdatedClient { get; set; } = "Outdated client! Please use {0}";
    /// <summary>
    /// Gets or sets the message template for a client protocol newer than the server's.
    /// </summary>
    public string OutdatedServer { get; set; } = "Outdated server! I'm still on {0}";
}

