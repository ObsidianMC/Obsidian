using Obsidian.API;

namespace Obsidian.Blocks
{
    public class BlockJukebox : Block
    {
        internal BlockJukebox(string name, int id) : base(name, id, Materials.Jukebox)
        {
        }
    }
}
