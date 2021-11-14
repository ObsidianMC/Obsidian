using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Actions.PlayerInfo;

public class InfoAction
{
    public Guid Uuid { get; set; }

    public virtual Task WriteAsync(MinecraftStream stream) => stream.WriteUuidAsync(this.Uuid);

    public virtual void Write(MinecraftStream stream) => stream.WriteUuid(Uuid);
}
