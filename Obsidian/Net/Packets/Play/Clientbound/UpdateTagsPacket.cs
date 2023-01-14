using Obsidian.Registries;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class UpdateTagsPacket : IClientboundPacket
{
    [Field(0)]
    public IDictionary<string, Tag[]> Tags { get; }

    public int Id => 0x6A;

    public static UpdateTagsPacket FromRegistry { get; } = new(TagsRegistry.Categories);

    public UpdateTagsPacket(IDictionary<string, Tag[]> tags)
    {
        this.Tags = tags;
    }
}

public class Tag
{
    public string Name { get; init; }
    public string Type { get; init; }
    public bool Replace { get; init; }
    public int[] Entries { get; init; }
    public int Count => Entries.Length;
}
