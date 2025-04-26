namespace Obsidian.WorldData.Decorators;

public class DecoratorFeatures
{
    /// <summary>
    /// List of Tree types and frequency of each.
    /// </summary>
    public List<TreeInfo> Trees { get; set; } = new List<TreeInfo>();

    /// <summary>
    /// List of Flora types
    /// </summary>
    public List<FloraInfo> Flora { get; set; } = new List<FloraInfo>();

    public class TreeInfo(int frequency, Type treeType)
    {
        /// <summary>
        /// Density of trees in the biome.
        /// 0 for none.
        /// 10 for a lot.
        /// </summary>
        public int Frequency { get; set; } = frequency;

        /// <summary>
        /// Which type of Tree
        /// </summary>
        public Type TreeType { get; set; } = treeType;
    }

    public class FloraInfo(int frequency, Type floraType, int radius, int density)
    {
        public int Frequency { get; set; } = frequency;

        public Type FloraType { get; set; } = floraType;

        public int Radius { get; set; } = radius;

        public int Density { get; set; } = density;
    }
}
