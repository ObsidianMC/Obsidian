namespace Obsidian.Net.Packets.Configuration.Clientbound;
public partial class RegistryDataPacket(string registryId, IDictionary<string, ICodec> codecs)
{
    public string RegistryId { get; } = registryId;
    public IDictionary<string, ICodec> Codecs { get; } = codecs;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteString(this.RegistryId);

        writer.WriteVarInt(this.Codecs.Count);

        foreach(var (key, codec) in this.Codecs)
        {
            writer.WriteString(key);

            writer.WriteBoolean(false);

            //writer.WriteCodec(codec);
        }
    }
}
