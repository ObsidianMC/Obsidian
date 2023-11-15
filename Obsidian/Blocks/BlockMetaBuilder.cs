using Obsidian.Nbt;
using System.Collections.ObjectModel;

namespace Obsidian.Blocks;

public class BlockMetaBuilder
{
    public ChatMessage? Name { get; internal set; }

    public NbtCompound BlockEntityTag { get; }
    public NbtCompound BlockStateTag { get; }

    public IReadOnlyList<ChatMessage> Lore { get; }

    public IReadOnlyList<string> CanPlaceOn { get; }

    private readonly List<string> canPlaceOn = [];

    private readonly List<ChatMessage> lore = [];

    public BlockMetaBuilder()
    {
        this.BlockEntityTag = new NbtCompound("BlockEntityTag");
        this.BlockStateTag = new NbtCompound("BlockStateTag");

        this.CanPlaceOn = new ReadOnlyCollection<string>(this.canPlaceOn);

        this.Lore = new ReadOnlyCollection<ChatMessage>(this.lore);
    }

    public BlockMetaBuilder CouldPlaceOn(string id)
    {
        this.canPlaceOn.Add(id);

        return this;
    }

    public BlockMetaBuilder WithName(string name)
    {
        this.Name = name;

        return this;
    }

    public BlockMetaBuilder AddLore(ChatMessage lore)
    {
        this.lore.Add(lore);

        return this;
    }

    public BlockMetaBuilder AddBlockStateTag(INbtTag tag)
    {
        this.BlockStateTag.Add(tag);

        return this;
    }

    public BlockMetaBuilder AddBlockEntityTag(INbtTag tag)
    {
        this.BlockEntityTag.Add(tag);

        return this;
    }

    public BlockMeta Build()
    {
        var meta = new BlockMeta
        {
            Name = this.Name,
            Lore = this.lore,
            CanPlaceOn = this.canPlaceOn,
            BlockEntityTag = this.BlockEntityTag,
            BlockStateTag = this.BlockStateTag
        };

        return meta;
    }
}
