using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Text;
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
                    Value = Encoding.UTF8.GetString(this.PluginData)
                },
                "minecraft:unregister" => new PluginMessageStore
                {
                    Type = PluginMessageType.UnRegister,
                    Value = Encoding.UTF8.GetString(this.PluginData)
                },
                _ => null
            };

            return result;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.Channel = await stream.ReadStringAsync();

            var length = stream.Length - stream.Position;

            this.PluginData = await stream.ReadUInt8ArrayAsync((int)length);
        }

        public async Task HandleAsync(Server server, Player player)
        {
            var result = await this.HandleAsync();

            if (result == null)
                return;

            switch (result.Type)
            {
                case PluginMessageType.Brand:
                    player.client.Brand = result.Value.ToString();
                    break;
                case PluginMessageType.Register:
                    {
                        var list = result.Value.ToString().Split("/");//Unsure if this is the only separator that's used

                        if (list.Length > 0)
                        {
                            foreach (var item in list)
                                server.RegisteredChannels.Add(item);
                        }
                        else
                            server.RegisteredChannels.Add(result.Value.ToString());

                        break;
                    }
                case PluginMessageType.UnRegister:
                    //TODO unregister registered channels 

                    //server.RegisteredChannels.RemoveWhere(x => x == this.Channel.ToLower());
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