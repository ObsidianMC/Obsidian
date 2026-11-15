namespace Obsidian.API;

/// <summary>What the client does when a player clicks a chat component.</summary>
public sealed class ClickComponent
{
    /// <summary>Gets or sets the kind of action.</summary>
    public required ClickAction Action { get; set; }

    /// <summary>Gets or sets the action's value, such as the URL to open or the command to run.</summary>
    public required string Value { get; set; }
}

/// <summary>A marker interface for click action values. Nothing implements it yet.</summary>
public interface ClickComponentValue
{

}
