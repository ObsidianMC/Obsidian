namespace Obsidian.Net.Actions.BossBar;

public class BossBarAction
{
    public Guid Uuid { get; set; }

    public int Action { get; }

    public BossBarAction(int action)
    {
        Action = action;
    }

    public virtual void WriteTo(MinecraftStream stream)
    {
        if (Uuid == default)
            throw new InvalidOperationException("Uuid must be assigned a value.");

        stream.WriteUuid(Uuid);
        stream.WriteVarInt(Action);
    }

    public virtual async Task WriteToAsync(MinecraftStream stream)
    {
        if (Uuid == default)
            throw new InvalidOperationException("Uuid must be assigned a value.");

        await stream.WriteUuidAsync(Uuid);
        await stream.WriteVarIntAsync(Action);
    }
}
