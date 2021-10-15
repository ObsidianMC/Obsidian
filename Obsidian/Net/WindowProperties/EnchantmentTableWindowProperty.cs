namespace Obsidian.Net.WindowProperties
{
    public class EnchantmentTableWindowProperty : IWindowProperty
    {
        public short Property { get; }

        public short Value { get; }

        public EnchantmentTableWindowProperty(EnchantmentTableProperty property, short value)
        {
            this.Property = (short)property;
            this.Value = value;
        }
    }

    public enum EnchantmentTableProperty : short
    {
        LevelRequirementTop,
        LevelRequirementMiddle,
        LevelRequirementBottom,

        EnchantmentSeed,

        EnchantmentIdOnHoverTop,
        EnchantmentIdOnHoverMiddle,
        EnchantmentIdOnHoverBottom,

        EnchantmentLevelOnHoverTop,
        EnchantmentLevelOnHoverMiddle,
        EnchantmentLevelOnHoverBottom,
    }
}
