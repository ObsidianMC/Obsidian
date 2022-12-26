namespace Obsidian.Net.Actions.PlayerInfo;

public abstract class InfoAction
{
    public abstract PlayerInfoAction Type { get; }

    public Guid Uuid { get; set; }

    public virtual Task WriteAsync(MinecraftStream stream) => stream.WriteUuidAsync(this.Uuid);

    public virtual void Write(MinecraftStream stream) => stream.WriteUuid(Uuid);
}
