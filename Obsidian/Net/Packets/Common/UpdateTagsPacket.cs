using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Common;
public partial record class UpdateTagsPacket
{
    [Field(0)]
    public IDictionary<string, Tag[]> Tags { get; init; } = default!;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(Tags.Count);

        foreach (var (resourceId, tags) in Tags)
        {
            var namespaceId = $"minecraft:{resourceId.TrimEnd('s')}";

            writer.WriteString(namespaceId);
            writer.WriteVarInt(tags.Length);

            foreach (var tag in tags)
            {
                // Tags go by their path in their registry (enchantable/armor in item, has_structure/igloo in
                // worldgen/biome), and vanilla's without their namespace.
                var name = tag.Namespace == "minecraft" ? tag.Path : tag.Identifier;

                writer.WriteString(name);
                // Read once: plugins loading or unloading replace a tag's entries meanwhile.
                var entries = tag.Entries;

                writer.WriteVarInt(entries.Length);
                foreach (var entry in entries)
                    writer.WriteVarInt(entry);
            }
        }
    }
}
