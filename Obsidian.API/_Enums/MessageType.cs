namespace Obsidian.API;

/// <summary>Where a chat message comes from or is shown.</summary>
public enum MessageType : int
{
    /// <summary>A message a player typed in chat.</summary>
    Chat,
    /// <summary>A message from the server.</summary>
    System,
    /// <summary>Text shown above the hotbar.</summary>
    ActionBar,
    /// <summary>A message sent with <c>/say</c>.</summary>
    SayCommand,
    /// <summary>A private message sent with <c>/msg</c>.</summary>
    MessageCommand,
    /// <summary>A team message sent with <c>/teammsg</c>.</summary>
    TeamMessageCommand,
    /// <summary>An action message sent with <c>/me</c>.</summary>
    EmoteCommand,
    /// <summary>Raw text sent with <c>/tellraw</c>.</summary>
    TellRawCommand
}
