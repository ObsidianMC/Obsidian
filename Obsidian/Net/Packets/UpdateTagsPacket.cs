using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets;
public sealed partial class UpdateTagsPacket(IDictionary<string, Tag[]> tags) : IClientboundPacket
{
    [Field(0)]
    public IDictionary<string, Tag[]> Tags { get; } = tags;

    //TODO FOR RELOADS 0x78 for play state
    public int Id { get; init; } = 0x0D;

    public static UpdateTagsPacket FromRegistry { get; } = new(Registries.TagsRegistry.Categories);
}
