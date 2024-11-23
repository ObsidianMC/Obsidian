namespace Obsidian.Net.Actions.PlayerInfo;

public class UpdateDisplayNameInfoAction(ChatMessage? displayName = null) : InfoAction
{
    public override PlayerInfoAction Type => PlayerInfoAction.UpdateDisplayName;

    public ChatMessage? DisplayName { get; init; } = displayName;
    public bool HasDisplayName => this.DisplayName != null;

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteBoolean(HasDisplayName);
        if (HasDisplayName)
            writer.WriteChat(DisplayName!);
    }
}
