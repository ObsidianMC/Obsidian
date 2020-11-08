using Obsidian.Blocks;
using Obsidian.Util.Registry;

namespace Obsidian
{
    public readonly struct SebastiansBlock
    {
        public Materials Material => (Materials)Registry.BaseToIdTable.Keys[baseId];

        private readonly short baseId;
        private readonly short state;

        internal SebastiansBlock(short stateId)
        {
            baseId = Registry.Ids[stateId];
            state = (short)(stateId - baseId);
        }

        public SebastiansBlock(Materials material)
        {
            baseId = Registry.BaseToIdTable.Values[(int)material];
            state = 0;
        }
    }
}
