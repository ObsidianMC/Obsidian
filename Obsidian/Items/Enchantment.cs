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
        BindingCurse,
        #endregion Armor

        #region Weapons
        Sharpness,
        Smite,
        BaneOfArthropods,
        Knockback,
        FireAspect,
        Looting,
        Sweeping,
        #endregion Weapons

        #region Tools
        Efficiency,
        SilkTouch,
        Unbreaking,
        Fortune,
        #endregion Tools

        #region Bow
        Power,
        Punch,
        Flame,
        Infinity,
        #endregion Bow

        #region Fishing Rod
        LuckOfTheSea,
        Lure,
        #endregion Fishing Rod

        #region Trident
        Loyalty,
        Impaling,
        Riptide,
        Channeling,
        #endregion Trident

        #region Crossbow
        Multishot,
        QuickCharge,
        Piercing,
        #endregion Crossbow

        #region All
        Mending,
        VanishingCurse,
        #endregion All
    }
}
