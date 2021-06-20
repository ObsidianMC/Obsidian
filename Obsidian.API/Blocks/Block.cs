using System;
using System.Diagnostics;

namespace Obsidian.API.Blocks
{
    [DebuggerDisplay("{Name,nq}:{Id}")]
    public readonly struct Block : IEquatable<Block>
    {
        public static Block Air => new Block(0, 0);

        private static short[] interactables;
        private static bool initialized = false;

        internal static string[] BlockNames;
        internal static MatchTarget[] StateToMatch;
        internal static short[] NumericToBase;

        public string UnlocalizedName => BlockNames[Id];
        public string Name => Material.ToString();
        public Material Material => (Material)StateToMatch[baseId].Numeric;
        public bool IsInteractable => (baseId >= 9276 && baseId <= 9372) || Array.BinarySearch(interactables, baseId) > -1;
        public bool IsAir => baseId == 0 || baseId == 9670 || baseId == 9669;
        public bool IsFluid => StateId > 33 && StateId < 66;
        public int Id => StateToMatch[baseId].Numeric;
        public short StateId => (short)(baseId + state);
        public int State => state;
        public short BaseId => baseId;

        private readonly short baseId;
        private readonly short state;

        internal Block(int stateId) : this((short)stateId)
        {
        }

        internal Block(short stateId)
        {
            baseId = StateToMatch[stateId].Base;
            state = (short)(stateId - baseId);
        }

        internal Block(int baseId, int state) : this((short)baseId, (short)state)
        {
        }

        internal Block(short baseId, short state)
        {
            this.baseId = baseId;
            this.state = state;
        }

        public Block(Material material, short state = 0)
        {
            baseId = NumericToBase[(int)material];
            this.state = state;
        }

        public override string ToString()
        {
            return UnlocalizedName;
        }

        public override int GetHashCode()
        {
            return StateId;
        }

        public bool Is(Material material)
        {
            return StateToMatch[baseId].Numeric == (int)material;
        }

        public override bool Equals(object obj)
        {
            return (obj is Block block) && block.StateId == StateId;
        }

        public bool Equals(Block other)
        {
            return other.StateId == StateId;
        }

        public static bool operator ==(Block a, Block b)
        {
            return a.StateId == b.StateId;
        }

        public static bool operator !=(Block a, Block b)
        {
            return a.StateId != b.StateId;
        }

        internal static void Initialize()
        {
            if (initialized)
                return;
            initialized = true;

            interactables = new[]
            {
                NumericToBase[(int)Material.Chest],
                NumericToBase[(int)Material.CraftingTable],
                NumericToBase[(int)Material.Furnace],
                NumericToBase[(int)Material.BrewingStand],
                NumericToBase[(int)Material.EnderChest],
                NumericToBase[(int)Material.Anvil],
                NumericToBase[(int)Material.ChippedAnvil],
                NumericToBase[(int)Material.DamagedAnvil],
                NumericToBase[(int)Material.TrappedChest],
                NumericToBase[(int)Material.Hopper],
                NumericToBase[(int)Material.Barrel],
                NumericToBase[(int)Material.Smoker],
                NumericToBase[(int)Material.BlastFurnace],
                NumericToBase[(int)Material.Grindstone],
            };
        }
    }
}
