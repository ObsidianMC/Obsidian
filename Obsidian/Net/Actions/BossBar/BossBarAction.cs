namespace Obsidian.Net.Actions.BossBar;

public abstract class BossBarAction
{
    public Guid Uuid { get; set; }

    public int Action { get; }

    public BossBarAction(int action)
    {
        this.Action = action;
    }

    public virtual void WriteTo(INetStreamWriter writer)
    {
        if (this.Uuid == default)
            throw new InvalidOperationException("Uuid must be assigned a value.");

        writer.WriteUuid(this.Uuid);
        writer.WriteVarInt(this.Action);
    }
}
