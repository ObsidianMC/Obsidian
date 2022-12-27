namespace Obsidian.Net.Actions.PlayerInfo;

public abstract class InfoAction
{
    public abstract PlayerInfoAction Type { get; }

    public virtual Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

    public virtual void Write(MinecraftStream stream) { }
}
