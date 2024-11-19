using Obsidian.Entities;
using System.Text;

namespace Obsidian.Net.Packets.Common;

public partial record class CustomPayloadPacket
{
    public string Channel { get; private set; }

    public byte[] PluginData { get; private set; }

    public CustomPayloadPacket()
    {
    }

    public CustomPayloadPacket(string channel, byte[] data)
    {
        Channel = channel;
        PluginData = data;
    }

    public PluginMessageStore Handle()
    {
        using var stream = new MinecraftStream(PluginData);

        var result = Channel switch
        {
            "minecraft:brand" => new PluginMessageStore
            {
                Type = PluginMessageType.Brand,
                Value = stream.ReadString()
            },
            "minecraft:register" => new PluginMessageStore // Payload should be a list of strings
            {
                Type = PluginMessageType.Register,
                Value = Encoding.UTF8.GetString(PluginData)
            },
            "minecraft:unregister" => new PluginMessageStore
            {
                Type = PluginMessageType.Unregister,
                Value = Encoding.UTF8.GetString(PluginData)
            },
            _ => null
        };

        return result;
    }

    public override void Populate(MinecraftStream stream)
    {
        Channel = stream.ReadString();
        PluginData = stream.ReadUInt8Array((int)(stream.Length - stream.Position));
    }

    public override ValueTask HandleAsync(Server server, Player player)
    {
        var result = Handle();

        if (result == null)
            return ValueTask.CompletedTask;

        switch (result.Type)
        {
            case PluginMessageType.Brand:
                player.client.Brand = result.Value.ToString();
                break;

            case PluginMessageType.Register:
                {
                    var list = result.Value.ToString().Split("/"); // Unsure if this is the only separator that's used

                    if (list.Length > 0)
                        foreach (var item in list)
                            server.RegisteredChannels.Add(item);
                    else
                        server.RegisteredChannels.Add(result.Value.ToString());

                    break;
                }

            case PluginMessageType.Unregister:
                // TODO unregister registered channels 

                //server.RegisteredChannels.RemoveWhere(x => x == this.Channel.ToLower());
                break;

            case PluginMessageType.Custom: // This can be ignored for now
                break;
        }

        return ValueTask.CompletedTask;
    }
}

public enum PluginMessageType
{
    Brand,
    Register,
    Unregister,
    Custom
}

public class PluginMessageStore
{
    public PluginMessageType Type { get; init; }
    public object Value { get; init; }
}
