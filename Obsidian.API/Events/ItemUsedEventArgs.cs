using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.API.Events
{
    public sealed class ItemUsedEventArgs : PlayerEventArgs, ICancellable
    {
        /// <summary>
        /// The item that was used.
        /// </summary>
        public ItemStack Item { get; set; }

        /// <summary>
        /// The slot that the item used was in, ranging from 0-9.
        /// 0-8 = Hotbar
        /// 9 = Offhand
        /// </summary>
        public short Slot { get; set; }

        public bool Cancel { get; set; }

        public ItemUsedEventArgs(IPlayer player, ItemStack item, short slot) : base(player)
        {
            this.Item = item;
            this.Slot = slot;
        }
    }
}
