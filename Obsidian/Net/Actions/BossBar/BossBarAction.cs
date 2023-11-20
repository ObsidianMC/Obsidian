﻿namespace Obsidian.Net.Actions.BossBar;

public abstract class BossBarAction
{
    public Guid Uuid { get; set; }

    public int Action { get; }

    public BossBarAction(int action)
    {
        this.Action = action;
    }

    public virtual void WriteTo(MinecraftStream stream)
    {
        if (this.Uuid == default)
            throw new InvalidOperationException("Uuid must be assigned a value.");

        stream.WriteUuid(this.Uuid);
        stream.WriteVarInt(this.Action);
    }

    public async virtual Task WriteToAsync(MinecraftStream stream)
    {
        if (this.Uuid == default)
            throw new InvalidOperationException("Uuid must be assigned a value.");

        await stream.WriteUuidAsync(this.Uuid);
        await stream.WriteVarIntAsync(this.Action);
    }
}
