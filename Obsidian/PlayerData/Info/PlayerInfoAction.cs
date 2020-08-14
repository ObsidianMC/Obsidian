using Obsidian.Net;
using Obsidian.Serializer.Attributes;
using System;
using System.Threading.Tasks;

namespace Obsidian.PlayerData.Info
{
    public class PlayerInfoAction
    {
        public string Uuid { get; set; }

        public virtual async Task WriteAsync(MinecraftStream stream) => await stream.WriteUuidAsync(Guid.Parse(this.Uuid));
    }
}