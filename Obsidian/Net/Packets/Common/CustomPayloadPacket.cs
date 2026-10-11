namespace Obsidian.Net.Packets.Common;

/// <summary>
/// A payload on a plugin channel. The connection's <see cref="Payloads.ClientChannels"/> handles received ones.
/// </summary>
public partial record class CustomPayloadPacket
{
    public string Channel { get; set; } = default!;

    public ReadOnlyMemory<byte> PluginData { get; set; }

    public CustomPayloadPacket() { }

    public CustomPayloadPacket(string channel, ReadOnlyMemory<byte> data)
    {
        Channel = channel;
        PluginData = data;
    }

    public override void Populate(INetStreamReader reader)
    {
        Channel = reader.ReadString();
        PluginData = reader.ReadUInt8Array((int)(reader.Size - reader.Offset));
    }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteString(this.Channel);
        writer.WriteByteArray(this.PluginData.Span);
    }
}
