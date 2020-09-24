using Obsidian.Items;

namespace Obsidian.Entities
{
    public class Fireworks : Entity
    {
        public ItemStack Item { get; private set; }

        public int Rotation { get; private set; } = 0;
    }
}
