using Obsidian.Util;
using Obsidian.Util.DataTypes;

namespace Obsidian.Entities
{
    public class EnderCrystal : Entity
    {
        public Position BeamTarget { get; private set; }

        public bool ShowBottom { get; private set; } = true;
    }
}
