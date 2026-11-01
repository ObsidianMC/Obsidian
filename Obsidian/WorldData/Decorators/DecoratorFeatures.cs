namespace Obsidian.WorldData.Decorators;

public class DecoratorFeatures
{
    /// <summary>
    /// List of Tree features and frequency of each.
    /// </summary>
    public List<TreeInfo> Trees { get; set; } = new List<TreeInfo>();

    /// <summary>
    /// List of Flora types
    /// </summary>
    public List<FloraInfo> Flora { get; set; } = new List<FloraInfo>();

    public class TreeInfo
    {
        /// <summary>
        /// Density of trees in the biome.
        /// 0 for none.
        /// 10 for a lot.
        /// </summary>
        public int Frequency { get; set; }

        /// <summary>
        /// The identifier of the configured tree feature to place, like <c>minecraft:oak</c>. It's looked up as it's
        /// placed, so plugins' and data packs' files overriding it apply.
        /// </summary>
        public string Feature { get; set; }

        public TreeInfo(int frequency, string feature)
        {
            Frequency = frequency;
            Feature = feature;
        }
    }

    public class FloraInfo(int frequency, Type floraType, int radius, int density)
    {
        public int Frequency { get; set; } = frequency;

        public Type FloraType { get; set; } = floraType;

        public int Radius { get; set; } = radius;

        public int Density { get; set; } = density;
    }
}
