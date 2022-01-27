using Obsidian.Nbt;
using System.Collections.ObjectModel;

namespace Obsidian.Blocks;

public class BlockMetaBuilder
{
    public ChatMessage Name { get; internal set; }

    public NbtCompound BlockEntityTag { get; }
    public NbtCompound BlockStateTag { get; }

    public IReadOnlyList<ChatMessage> Lore { get; }

    public IReadOnlyList<string> CanPlaceOn { get; }

    private readonly List<string> canPlaceOn = new List<string>();

    private readonly List<ChatMessage> lore = new List<ChatMessage>();

    public BlockMetaBuilder()
    {
        BlockEntityTag = new NbtCompound("BlockEntityTag");
        BlockStateTag = new NbtCompound("BlockStateTag");

        CanPlaceOn = new ReadOnlyCollection<string>(canPlaceOn);

        Lore = new ReadOnlyCollection<ChatMessage>(lore);
    }

    public BlockMetaBuilder CouldPlaceOn(string id)
    {
        canPlaceOn.Add(id);

        return this;
    }

    public BlockMetaBuilder WithName(string name)
    {
        Name = name;

        return this;
    }

    public BlockMetaBuilder AddLore(ChatMessage lore)
    {
        this.lore.Add(lore);

        return this;
    }

    public BlockMetaBuilder AddBlockStateTag(INbtTag tag)
    {
        BlockStateTag.Add(tag);

        return this;
    }

    public BlockMetaBuilder AddBlockEntityTag(INbtTag tag)
    {
        BlockEntityTag.Add(tag);

        return this;
    }

    public BlockMeta Build()
    {
        var meta = new BlockMeta
        {
            Name = Name,
            Lore = lore,
            CanPlaceOn = canPlaceOn,
            BlockEntityTag = BlockEntityTag,
            BlockStateTag = BlockStateTag
        };

        return meta;
    }
}
