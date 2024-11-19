using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Common;
public partial record class UpdateTagsPacket
{
    [Field(0)]
    public IDictionary<string, Tag[]> Tags { get; init; } = default!;
}
