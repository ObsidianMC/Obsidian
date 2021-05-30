using Obsidian.Serialization.Attributes;
using System.Collections.Generic;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class UnlockRecipes : IClientboundPacket
    {
        [Field(0), ActualType(typeof(int)), VarLength]
        public UnlockRecipeAction Action { get; set; }

        [Field(1)]
        public bool CraftingeBookOpen { get; set; }

        [Field(2)]
        public bool CraftingBookFilterActive { get; set; }

        [Field(3)]
        public bool SmeltingBookOpen { get; set; }

        [Field(4)]
        public bool SmeltingBookFilterActive { get; set; }

        [Field(5)]
        public bool BlastFurnaceBookOpen { get; set; }

        [Field(6)]
        public bool BlastFurnaceBookFilterActive { get; set; }

        [Field(7)]
        public bool SmokerBookOpen { get; set; }

        [Field(8)]
        public bool SmokerBookFilterActive { get; set; }

        [Field(9)]
        public List<string> FirstRecipeIds { get; set; }

        [Field(10)]
        public List<string> SecondRecipeIds { get; set; }

        public int Id => 0x35;
    }

    public enum UnlockRecipeAction : int
    {
        Init,

        Add,

        Remove
    }
}
