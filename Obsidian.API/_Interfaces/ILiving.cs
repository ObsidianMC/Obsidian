namespace Obsidian.API
{
    public interface ILiving : IEntity
    {
        public LivingBitMask LivingBitMask { get; set; }

        public float Health { get; set; }

        public uint ActiveEffectColor { get; }

        public bool AmbientPotionEffect { get; set; }

        public int AbsorbedArrows { get; set; }

        public int AbsorbtionAmount { get; set; }

        public Vector BedBlockPosition { get; set; }
    }
}
