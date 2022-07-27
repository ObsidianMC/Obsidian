using Obsidian.Serialization.Attributes;
using System.Text;

namespace Obsidian.Net.Packets.Play;

public partial class PluginMessagePacket : IClientboundPacket
{
    [Field(0)]
    public string Channel { get; private set; }

    [Field(1)]
    public byte[] PluginData { get; private set; }

    public int Id => 0x15;

    public PluginMessagePacket()
    {
    }

    public PluginMessagePacket(string channel, byte[] data)
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

    public void Populate(MinecraftStream stream)
    {
        Channel = stream.ReadString();
        PluginData = stream.ReadUInt8Array((int)(stream.Length - stream.Position));
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
