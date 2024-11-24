
namespace Obsidian.Net.Packets.Configuration.Clientbound;
public partial class SelectKnownPacksPacket()
{
    public required List<KnownPack> KnownPacks { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(KnownPacks.Count);

        foreach(var knownPack in KnownPacks)
        {
            writer.WriteString(knownPack.Namespace);
            writer.WriteString(knownPack.Id);
            writer.WriteString(knownPack.Version);
        }
    }
}

public readonly struct KnownPack
{
    public required string Namespace { get; init; }

    public required string Id { get; init; }

    public required string Version { get; init; }
}
