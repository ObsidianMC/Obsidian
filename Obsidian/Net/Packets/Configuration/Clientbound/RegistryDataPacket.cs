namespace Obsidian.Net.Packets.Configuration.Clientbound;

/// <summary>
/// Sends the entries of a synchronized registry. Entries are sent by id only unless <see cref="WriteCodecs"/> is set; the
/// client then takes their data from the vanilla <c>minecraft:core</c> pack it shares with the server. The order of the
/// entries gives their network ids.
/// </summary>
public partial class RegistryDataPacket
{
    private readonly IReadOnlyList<string> entryIds;

    public string RegistryId { get; }

    public IDictionary<string, ICodec> Codecs { get; }

    public bool WriteCodecs { get; set; }

    public RegistryDataPacket(string registryId, IDictionary<string, ICodec> codecs)
    {
        this.RegistryId = registryId;
        this.Codecs = codecs;
        this.entryIds = [.. codecs.Keys];
    }

    /// <summary>
    /// Creates a packet that sends <paramref name="entryIds"/>, in order, without data.
    /// </summary>
    public RegistryDataPacket(string registryId, IEnumerable<string> entryIds)
    {
        this.RegistryId = registryId;
        this.Codecs = new Dictionary<string, ICodec>();
        this.entryIds = [.. entryIds];
    }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteString(this.RegistryId);

        writer.WriteVarInt(this.entryIds.Count);

        foreach (var key in this.entryIds)
        {
            writer.WriteString(key);

            var writeCodec = this.WriteCodecs && this.Codecs.ContainsKey(key);
            writer.WriteBoolean(writeCodec);

            if (writeCodec)
                writer.WriteCodec(this.Codecs[key]);
        }
    }
}
