using Obsidian.Util.Extensions;

namespace Obsidian.Items
{
    public class Enchantment
    {
        internal string Id { get; set; }

        public EnchantmentType Type => this.Id.ToEnchantType();
        
        public int Level { get; set; }
    }

    public enum EnchantmentType
    {
        #region Armor
        Protection,
        FireProtection,
        FeatherFalling,
        BlastProtection,
        ProjectileProtection,
        Respiration,
        AquaInfinity,
        Thorns,
        DepthStrider,
        FrostWalker,
        CurseOfBinding,
        #endregion Armor

        #region Weapons
        Sharpness,
        Smite,
        BaneOfArthropods,
        Knockback,
        FireAspect,
        Looting,
        SweepingEdge,
        #endregion Weapons

        #region Tools
        Efficiency,
        SilkTOuch,
        Fortune,
        #endregion Tools

        #region Fishing Rod
        LuckOfTheSea,
        Lure,
        #endregion Fishing Rod

        #region Bow
        Power,
        Punch,
        Flame,
        Infinity,
        #endregion Bow

        #region Trident
        Channeling,
        Riptide,
        Impaling,
        Loyalty,
        #endregion Trident

        #region All
        Mending,
        VanishingCurse,
        Unbreaking
        #endregion All
    }
}
