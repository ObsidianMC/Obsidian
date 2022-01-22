namespace Obsidian.Net.Packets.Play.Clientbound;

public class MatchItem
{
    public MatchItem(string match, bool hasTooltip, ChatMessage? tooltip=null)
    {
        Match = match;
        HasTooltip = hasTooltip;
        Tooltip = tooltip;
    }

    public string Match { get; init; }
    public bool HasTooltip { get; init; }
    public ChatMessage? Tooltip { get; init; }
}
