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
                // Tags in a subfolder of their registry (type item/enchantable) go by their path in it
                // (enchantable/armor), and vanilla's without their namespace.
                var folderStart = tag.Type.IndexOf('/');
                var path = folderStart < 0 ? tag.Name : $"{tag.Type[(folderStart + 1)..]}/{tag.Name}";
                var name = tag.Namespace == "minecraft" ? path : $"{tag.Namespace}:{path}";

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
