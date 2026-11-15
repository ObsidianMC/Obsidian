using Obsidian.Nbt;

namespace Obsidian.API.Events;

/// <summary>Untrusted client input from a custom dialog or text click action.</summary>
public sealed class DialogActionEventArgs(IPlayer player, IServer server, string actionId, INbtTag? payload)
    : PlayerEventArgs(player, server)
{
    /// <summary>
    /// Gets the action id the client sent, as set on the dialog or text click action.
    /// </summary>
    public string ActionId { get; } = actionId;
    /// <summary>
    /// Gets the NBT payload the client sent with the action, or <see langword="null"/> if it sent none.
    /// </summary>
    public INbtTag? Payload { get; } = payload;
}
