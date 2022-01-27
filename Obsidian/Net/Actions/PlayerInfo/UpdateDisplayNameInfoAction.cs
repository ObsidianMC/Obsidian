namespace Obsidian.Net.Actions.PlayerInfo;

public class UpdateDisplayNameInfoAction : InfoAction
{
    public string DisplayName { get; set; }
    public bool HasDisplayName => string.IsNullOrWhiteSpace(DisplayName);

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteStringAsync(DisplayName);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteBoolean(HasDisplayName);
        if (HasDisplayName)
            stream.WriteString(DisplayName);
    }
}
