using Obsidian.Blocks;
using Obsidian.Util.Registry;
using System;

namespace Obsidian
{
    public readonly struct SebastiansBlock
    {
        private static short[] interactables;
        private static bool initialized;

        public Materials Material => (Materials)Registry.StateToMatch[baseId].numeric;
        public bool IsInteractable => Array.BinarySearch(interactables, baseId) > -1;
        public bool IsAir => baseId == 0 || baseId == 9670 || baseId == 9669;
        public int Id => Registry.StateToMatch[baseId].numeric;
        internal short StateId => (short)(baseId + state);

        private readonly short baseId;
        private readonly short state;

        internal SebastiansBlock(short stateId)
        {
            baseId = Registry.StateToMatch[stateId].@base;
            state = (short)(stateId - baseId);
        }

        public SebastiansBlock(Materials material)
        {
            baseId = Registry.NumericToBase[(int)material];
            state = 0;
        }

        public override string ToString()
        {
            return Material.ToString();
        }

        public static void Initialize()
        {
            if (initialized)
                return;
            initialized = true;

            interactables = new short[] { 2034, 3356, 3373, 5137, 5255, 6614, 6618, 6622, 6626, 14815, 14825, 14837 };
        }
    }
}
