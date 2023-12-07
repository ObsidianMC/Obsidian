using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets;
public sealed partial class UpdateTagsPacket : IClientboundPacket
{
    [Field(0)]
    public IDictionary<string, Tag[]> Tags { get; }

    //TODO FOR RELOADS 0x74 for play state
    public int Id { get; init; } = 0x08;

    public static UpdateTagsPacket FromRegistry { get; } = new(Registries.TagsRegistry.Categories);

    public UpdateTagsPacket(IDictionary<string, Tag[]> tags)
    {
        this.Tags = tags;
    }
}
