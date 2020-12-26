using Obsidian.API;

namespace Obsidian.Blocks
{
    public class BlockSnow : Block
    {
        internal BlockSnow(string name, int id) : base(name, id, Materials.Snow)
        {
        }
    }
}
