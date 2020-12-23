using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public partial class PluginMessage : IPacket
    {
        [Field(0)]
        public string Channel { get; private set; }

        [Field(1)]
        public byte[] PluginData { get; private set; }

        public int Id { get; set; } = 0x17;

        public PluginMessage()
        {
        }

        public PluginMessage(string channel, byte[] data)
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

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.Channel = await stream.ReadStringAsync();
            this.PluginData = await stream.ReadUInt8ArrayAsync();
        }

        public async Task HandleAsync(Obsidian.Server server, Player player)
        {
            var result = await this.HandleAsync();

            switch (result.Type)
            {
                case PluginMessageType.Brand:
                    player.client.Brand = result.Value.ToString();
                    break;
                case PluginMessageType.Register:
                    {
                        using var stream = new MinecraftStream(this.PluginData);
                        var len = await stream.ReadVarIntAsync();

                        //Idk how this would work so I'm assuming a length will be sent first
                        for (int i = 0; i < len; i++)
                            server.RegisteredChannels.Add(await stream.ReadStringAsync());

                        break;
                    }
                case PluginMessageType.UnRegister:
                    server.RegisteredChannels.RemoveWhere(x => x == this.Channel.ToLower());
                    break;
                case PluginMessageType.Custom://This can be ignored for now
                default:
                    break;
            }
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