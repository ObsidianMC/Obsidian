using Obsidian.Nbt;

namespace Obsidian.Blocks;

public struct BlockMeta
{
    public ChatMessage? Name { get; internal set; }
    public IReadOnlyList<ChatMessage> Lore { get; internal set; }

    public IReadOnlyList<string> CanPlaceOn { get; internal set; }

    // https://minecraft.wiki/w/Chunk_format#Block_entity_format
    public NbtCompound BlockEntityTag { get; internal set; }
    public NbtCompound BlockStateTag { get; internal set; }
}
