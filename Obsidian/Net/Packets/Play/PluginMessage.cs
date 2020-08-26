using Obsidian.Serializer.Attributes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public class PluginMessage : Packet
    {
        private readonly List<PluginMessageHandler> handlers = new List<PluginMessageHandler>()
        {
            new MinecraftBrand()
        };

        [Field(0)]
        public string Channel { get; private set; }

        [Field(1)]
        public byte[] PluginData { get; private set; }

        public PluginMessage(string channel, byte[] data) : base(0x19, data)
        {
            this.Channel = channel;
            this.PluginData = data;
        }
    }

    public abstract class PluginMessageHandler
    {
        public abstract string Channel { get; }

        public abstract Task HandleAsync(MinecraftStream stream);
    }

    public class MinecraftBrand : PluginMessageHandler
    {
        public override string Channel => "minecraft:brand";

        public override async Task HandleAsync(MinecraftStream stream) => await stream.ReadStringAsync();
    }
}