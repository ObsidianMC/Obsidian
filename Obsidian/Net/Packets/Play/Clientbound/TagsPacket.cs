using Obsidian.Serialization.Attributes;
using Obsidian.Utilities.Registry;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class TagsPacket : IClientboundPacket
{
    [Field(0)]
    public IDictionary<string, List<Tag>> Tags { get; }

    public int Id => 0x67;

    public static readonly TagsPacket FromRegistry = new(Registry.Tags);

    public TagsPacket(IDictionary<string, List<Tag>> tags)
    {
        this.Tags = tags;
    }
}

public class Tag
{
    public string Name { get; init; }
    public string Type { get; init; }
    public bool Replace { get; init; }
    public List<int> Entries { get; init; } = new();
    public int Count => Entries.Count;
}

public class RawTag
{
    public string Name { get; init; }
    public string Type { get; init; }
    public bool Replace { get; init; }
    public List<string> Values { get; set; }
}
