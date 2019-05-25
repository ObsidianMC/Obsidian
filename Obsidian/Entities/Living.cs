namespace Obsidian.Entities
{
    public class Living : Entity
    {
        //TODO: Hand States

        public float Health { get; set; }

        public object ActiveEffect { get; private set; }

        public bool AmbientPotionEffect { get; set; }

        public int Arrows { get; set; }
    }
}
