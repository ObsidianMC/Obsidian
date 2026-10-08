using Obsidian.Nbt;

namespace Obsidian.API.Events;

/// <summary>Untrusted client input from a custom dialog or text click action.</summary>
public sealed class DialogActionEventArgs(IPlayer player, IServer server, string actionId, INbtTag? payload)
    : PlayerEventArgs(player, server)
{
    public string ActionId { get; } = actionId;
    public INbtTag? Payload { get; } = payload;
}
