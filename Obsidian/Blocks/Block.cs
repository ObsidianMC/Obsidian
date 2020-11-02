using Obsidian.Util.DataTypes;
using System.Collections.Generic;

namespace Obsidian.Blocks
{
    public class Block : BlockState
    {
        public byte Metadata { get; set; }

        public List<BlockState> States { get; private set; } = new List<BlockState>();

        public Position Location { get; set; }

        internal Block(string name, int id, Materials type) : base(name, id, type) { }
        public void Set(Block block)
        {
            this.Id = block.Id;
        }

        internal bool CanInteract() => this.Type == Materials.CraftingTable || this.Type == Materials.Furnace ||
            this.Type == Materials.BlastFurnace || this.Type == Materials.Chest || this.Type == Materials.Anvil ||
            this.Type == Materials.ChippedAnvil || this.Type == Materials.DamagedAnvil || this.Type == Materials.EnderChest || 
            this.Type == Materials.TrappedChest || this.Type == Materials.Grindstone || this.Type == Materials.Lectern || this.Type == Materials.BrewingStand;
    }
}
