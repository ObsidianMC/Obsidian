using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Actions.PlayerInfo
{
    public class PlayerInfoAction
    {
        public Guid Uuid { get; set; }

        public virtual async Task WriteAsync(MinecraftStream stream) => await stream.WriteUuidAsync(this.Uuid);

        public virtual void Write(MinecraftStream stream) => stream.WriteUuid(Uuid);
    }
}