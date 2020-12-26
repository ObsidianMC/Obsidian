using Obsidian.API;

namespace Obsidian.Blocks
{
    public class BlockWaterLily : Block
    {
        internal BlockWaterLily(string name, int id) : base(name, id, Materials.LilyPad)
        {
        }
    }
}
