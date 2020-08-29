using fNbt;
using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.Items
{
    public class Slot
    {
        public bool Present { get; set; }

        public int ItemId { get; set; }

        public byte ItemCount { get; set; }

        public NbtTag Nbt { get; set; }
    }
}
