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

        // The payload is the rest of the packet, which may be empty (NeoForge's frozen_registry_sync_completed is);
        // ReadUInt8Array reads a length prefix when asked for none.
        var length = (int)(reader.Size - reader.Offset);
        PluginData = length > 0 ? reader.ReadUInt8Array(length) : ReadOnlyMemory<byte>.Empty;
    }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteString(this.Channel);
        writer.WriteByteArray(this.PluginData.Span);
    }
}
