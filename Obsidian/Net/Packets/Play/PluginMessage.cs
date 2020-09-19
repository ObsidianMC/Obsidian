using Obsidian.Serializer.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public class PluginMessage : Packet
    {
        [Field(0)]
        public string Channel { get; private set; }

        [Field(1)]
        public byte[] PluginData { get; private set; }

        public PluginMessage() : base(0x19) { }

        public PluginMessage(string channel, byte[] data) : base(0x19)
        {
            this.Channel = channel;
            this.PluginData = data;
        }

        public async ValueTask<PluginMessageStore> HandleAsync()
        {
            using var stream = new MinecraftStream(this.PluginData);
            var result = this.Channel switch
            {
                "minecraft:brand" => new PluginMessageStore
                {
                    Type = PluginMessageType.Brand,
                    Value = await stream.ReadStringAsync()
                },
                "minecraft:register" => new PluginMessageStore//Payload should be a list of strings
                {
                    Type = PluginMessageType.Register,
                    Value = this.PluginData
                },
                "minecraft:unregister" => new PluginMessageStore
                {
                    Type = PluginMessageType.UnRegister
                },
                _ => throw new System.NotImplementedException()
            };

            return result;
        }
    }

    public enum PluginMessageType
    {
        Brand,
        Register,
        UnRegister,
        Custom
    }

    public class PluginMessageStore
    {
        public PluginMessageType Type { get; set; }

        public object Value { get; set; }
    }
}