using Obsidian.Nbt;

namespace Obsidian.Net.Packets.Configuration.Clientbound;

/// <summary>
/// Sends the entries of a synchronized registry. Entries are sent by id only unless <see cref="WriteCodecs"/> is set
/// or they're given data; the client then takes their data from the vanilla <c>minecraft:core</c> pack it shares with
/// the server. The order of the entries gives their network ids.
/// </summary>
public partial class RegistryDataPacket
{
    private readonly IReadOnlyList<string> entryIds;

    // Each entry's data, or null to send it by id: the NBT of plugins' data files.
    private readonly IReadOnlyList<NbtCompound?>? entryData;

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

    /// <summary>
    /// Creates a packet that sends <paramref name="entries"/>, in order, each with its data, or by id where it has
    /// none.
    /// </summary>
    public RegistryDataPacket(string registryId, IEnumerable<(string Id, NbtCompound? Data)> entries)
    {
        var list = entries.ToList();

        this.RegistryId = registryId;
        this.Codecs = new Dictionary<string, ICodec>();
        this.entryIds = [.. list.Select(entry => entry.Id)];
        this.entryData = [.. list.Select(entry => entry.Data)];
    }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteString(this.RegistryId);

        writer.WriteVarInt(this.entryIds.Count);

        for (var i = 0; i < this.entryIds.Count; i++)
        {
            var key = this.entryIds[i];
            writer.WriteString(key);

            if (this.entryData?[i] is NbtCompound data)
            {
                writer.WriteBoolean(true);
                writer.WriteNbtCompound(data);
                continue;
            }

            var writeCodec = this.WriteCodecs && this.Codecs.ContainsKey(key);
            writer.WriteBoolean(writeCodec);

            if (writeCodec)
                writer.WriteCodec(this.Codecs[key]);
        }
    }
}
