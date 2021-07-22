﻿using Obsidian.API;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class SetSlot : IClientboundPacket
    {
        /// <summary>
        /// 0 for player inventory. -1 For the currently dragged item.
        /// If the window ID is set to -2, then any slot in the inventory can be used but no add item animation will be played.
        /// </summary>
        [Field(0)]
        public byte WindowId { get; init; } = 0;

        /// <summary>
        /// Can be -1 to set the currently dragged item.
        /// </summary>
        [Field(1)]
        public short Slot { get; init; }

        [Field(2)]
        public ItemStack SlotData { get; init; }

        public int Id => 0x15;
    }
}
