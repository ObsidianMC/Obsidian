using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Common;
public partial record class UpdateTagsPacket
{
    [Field(0)]
    public IDictionary<string, Tag[]> Tags { get; init; } = default!;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(Tags.Count - 1);

        foreach (var (resourceId, tags) in Tags)
        {
            if (resourceId == "worldgen")
                continue;

            var namespaceId = $"minecraft:{resourceId.TrimEnd('s')}";

            writer.WriteString(namespaceId);
            writer.WriteVarInt(tags.Length);

            foreach (var tag in tags)
            {
                writer.WriteString(tag.Name);
                writer.WriteVarInt(tag.Count);
                for (int i = 0; i < tag.Entries.Length; i++)
                {
                    writer.WriteVarInt(tag.Entries[i]);
                }
            }
        }
    }
}
