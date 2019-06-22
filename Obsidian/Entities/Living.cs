namespace Obsidian.Entities
{
    public class Living : Entity
    {
        public bool HandActive { get; set; }

        public HandState ActiveHand { get; set; }

        public float Health { get; set; }

        public uint ActiveEffectColor { get; private set; }

        public bool AmbientPotionEffect { get; set; }

        public int Arrows { get; set; }
    }

    public enum HandState
    {
    }
}
