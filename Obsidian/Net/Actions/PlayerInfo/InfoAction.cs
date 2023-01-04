namespace Obsidian.Net.Actions.PlayerInfo;

public abstract class InfoAction
{
    public abstract PlayerInfoAction Type { get; }

    public abstract Task WriteAsync(MinecraftStream stream);

    public abstract void Write(MinecraftStream stream);
}
