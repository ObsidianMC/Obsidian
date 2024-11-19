using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Common;
public partial record class UpdateTagsPacket(IDictionary<string, Tag[]> tags)
{
    [Field(0)]
    public IDictionary<string, Tag[]> Tags { get; } = tags;

    public static UpdateTagsPacket FromRegistry { get; } = new(Registries.TagsRegistry.Categories);
}
