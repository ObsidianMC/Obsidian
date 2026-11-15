namespace Obsidian.API.Configuration;
/// <summary>
/// Configures join and leave message formats and retains currently unused connection rejection templates.
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
    /// Gets or sets the currently unused whitelist rejection template; whitelist checks use hard-coded text.
    /// </summary>
    public string NotWhitelisted { get; set; } = "You are not whitelisted on this server!";

    /// <summary>
    /// Gets or sets the currently unused template for rejecting a connection when the server is full.
    /// </summary>
    public string ServerFull { get; set; } = "The server is full!";

    /// <summary>
    /// Gets or sets the currently unused template for an older client protocol; handshakes use hard-coded text.
    /// </summary>
    public string OutdatedClient { get; set; } = "Outdated client! Please use {0}";
    /// <summary>
    /// Gets or sets the currently unused template for a newer client protocol; handshakes use hard-coded text.
    /// </summary>
    public string OutdatedServer { get; set; } = "Outdated server! I'm still on {0}";
}

