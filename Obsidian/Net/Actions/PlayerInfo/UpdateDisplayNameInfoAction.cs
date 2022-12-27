namespace Obsidian.Net.Actions.PlayerInfo;

public class UpdateDisplayNameInfoAction : InfoAction
{
    public override PlayerInfoAction Type => PlayerInfoAction.UpdateDisplayName;

    public ChatMessage? DisplayName { get; init; }
    public bool HasDisplayName => this.DisplayName != null;

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await stream.WriteBooleanAsync(this.HasDisplayName);
        if (this.HasDisplayName)
            await stream.WriteChatAsync(this.DisplayName);
    }

    public override void Write(MinecraftStream stream)
    {
        stream.WriteBoolean(HasDisplayName);
        if (HasDisplayName)
            stream.WriteChat(DisplayName);
    }
}
