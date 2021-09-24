using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Actions.BossBar
{
    public class BossBarAction
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

        public virtual async Task WriteToAsync(MinecraftStream stream)
        {
            if (this.Uuid == default)
                throw new InvalidOperationException("Uuid must be assigned a value.");

            await stream.WriteUuidAsync(this.Uuid);
            await stream.WriteVarIntAsync(this.Action);
        }
    }
}
