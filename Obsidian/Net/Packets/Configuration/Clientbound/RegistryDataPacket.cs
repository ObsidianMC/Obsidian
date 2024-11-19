namespace Obsidian.Net.Packets.Configuration.Clientbound;
public partial class RegistryDataPacket(string registryId, IDictionary<string, ICodec> codecs)
{
    public string RegistryId { get; } = registryId;
    public IDictionary<string, ICodec> Codecs { get; } = codecs;

    public override void Serialize(MinecraftStream stream)
    {
        using var packetStream = new MinecraftStream();

        packetStream.WriteString(this.RegistryId);

        packetStream.WriteVarInt(this.Codecs.Count);

        foreach(var (key, codec) in this.Codecs)
        {
            packetStream.WriteString(key);

            packetStream.WriteBoolean(true);

            packetStream.WriteCodec(codec);
        }
        
        stream.Lock.Wait();
        stream.WriteVarInt(Id.GetVarIntLength() + (int)packetStream.Length);
        stream.WriteVarInt(Id);
        packetStream.Position = 0;
        packetStream.CopyTo(stream);
        stream.Lock.Release();
    }
}
