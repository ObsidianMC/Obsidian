using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public class PluginMessage : Packet
    {
        public List<PluginMessageHandler> Handlers = new List<PluginMessageHandler>()
        {
            new MinecraftBrand()
        };

        public string Channel { get; private set; }

        public byte[] Data { get; private set; }

        public PluginMessage(string channel, byte[] data) : base(0x19, Array.Empty<byte>())
        {
            //TODO: ADD check pls
            this.Channel = channel;
            this.Data = data;
        }

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WriteIdentifierAsync(Channel);
            await stream.WriteAsync(Data);
        }

        protected override async Task PopulateAsync(MinecraftStream stream)
        {
            this.Channel = await stream.ReadIdentifierAsync();

            await Handlers.First(h => h.Channel == this.Channel).HandleAsync(stream);
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