using System;
using System.Diagnostics;

namespace Obsidian.API.Blocks
{
    [DebuggerDisplay("{Name,nq}:{Id}")]
    public readonly struct Block : IEquatable<Block>
    {
        public static Block Air => new Block(0, 0);

        private static ushort[] interactables;
        private static bool initialized = false;

        internal static string[] Names;
        internal static ushort[] StateToBase;
        internal static ushort[] StateToNumeric;
        internal static ushort[] NumericToBase;

        public string UnlocalizedName => Names[Id];
        public string Name => Material.ToString();
        public Material Material => (Material)StateToNumeric[baseId];
        public bool IsInteractable => (baseId >= 9276 && baseId <= 9372) || Array.BinarySearch(interactables, baseId) > -1;
        public bool IsAir => baseId == 0 || baseId == 9670 || baseId == 9669;
        public bool IsFluid => StateId > 33 && StateId < 66;
        public uint Id => StateToNumeric[baseId];
        public ushort StateId => (ushort)(baseId + state);
        public uint State => state;
        public ushort BaseId => baseId;

        private readonly ushort baseId;
        private readonly ushort state;

        internal Block(int stateId) : this((ushort)stateId)
        {
        }

        internal Block(uint stateId) : this((ushort)stateId)
        {
        }

        internal Block(ushort stateId)
        {
            baseId = StateToBase[stateId];
            state = (ushort)(stateId - baseId);
        }

        internal Block(int baseId, int state) : this((ushort)baseId, (ushort)state)
        {
        }

        internal Block(uint baseId, uint state) : this((ushort)baseId, (ushort)state)
        {
        }

        internal Block(ushort baseId, ushort state)
        {
            this.baseId = baseId;
            this.state = state;
        }

        public Block(Material material)
        {
            baseId = NumericToBase[(uint)material];
            state = 0;
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
            return StateToNumeric[baseId] == (uint)material;
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
                NumericToBase[(uint)Material.Chest],
                NumericToBase[(uint)Material.CraftingTable],
                NumericToBase[(uint)Material.Furnace],
                NumericToBase[(uint)Material.BrewingStand],
                NumericToBase[(uint)Material.EnderChest],
                NumericToBase[(uint)Material.Anvil],
                NumericToBase[(uint)Material.ChippedAnvil],
                NumericToBase[(uint)Material.DamagedAnvil],
                NumericToBase[(uint)Material.TrappedChest],
                NumericToBase[(uint)Material.Hopper],
                NumericToBase[(uint)Material.Barrel],
                NumericToBase[(uint)Material.Smoker],
                NumericToBase[(uint)Material.BlastFurnace],
                NumericToBase[(uint)Material.Grindstone],
            };
        }
    }
}
