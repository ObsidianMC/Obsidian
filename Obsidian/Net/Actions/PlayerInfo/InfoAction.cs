namespace Obsidian.Net.Actions.PlayerInfo;

public abstract class InfoAction
{
    public abstract PlayerInfoAction Type { get; }

    public abstract void Write(INetStreamWriter writer);
}
