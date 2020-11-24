using Obsidian.Chat;
using Obsidian.Nbt.Tags;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Obsidian.Blocks
{
    public struct BlockMeta
    {
        internal Guid InventoryId { get; set; }

        public ChatMessage Name { get; internal set; }
        public IReadOnlyList<ChatMessage> Lore { get; internal set; }

        public IReadOnlyList<string> CanPlaceOn { get; internal set; }

        //https://minecraft.gamepedia.com/Chunk_format#Block_entity_format
        public NbtCompound BlockEntityTag { get; internal set; }
        public NbtCompound BlockStateTag { get; internal set; }
    }

    public class BlockMetaBuilder
    {
        internal Guid InventoryId { get; set; }

        public ChatMessage Name { get; internal set; }

        public NbtCompound BlockEntityTag { get; }
        public NbtCompound BlockStateTag { get; }

        public IReadOnlyList<ChatMessage> Lore { get; }

        public IReadOnlyList<string> CanPlaceOn { get; }

        private readonly List<string> canPlaceOn = new List<string>();

        private readonly List<ChatMessage> lore = new List<ChatMessage>();

        public BlockMetaBuilder()
        {
            this.BlockEntityTag = new NbtCompound("BlockEntityTag");
            this.BlockStateTag = new NbtCompound("BlockStateTag");

            this.CanPlaceOn = new ReadOnlyCollection<string>(this.canPlaceOn);

            this.Lore = new ReadOnlyCollection<ChatMessage>(this.lore);
        }

        internal BlockMetaBuilder WithInventoryId(Guid id)
        {
            this.InventoryId = id;

            return this;
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

        public BlockMetaBuilder AddBlockStateTag(NbtTag tag)
        {
            this.BlockStateTag.Add(tag);

            return this;
        }

        public BlockMetaBuilder AddBlockEntityTag(NbtTag tag)
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
                InventoryId = this.InventoryId,
                CanPlaceOn = this.canPlaceOn,
                BlockEntityTag = this.BlockEntityTag,
                BlockStateTag = this.BlockStateTag
            };

            return meta;
        }

    }
}
