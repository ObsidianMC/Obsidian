namespace Obsidian.Entities
{
    public class Fireworks : Entity
    {
        public object Item { get; private set; }//TODO: ItemStack

        public int Rotation { get; private set; } = 0;
    }
}
