using Obsidian.API.World.Features.Tree;

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
        /// Which TreeFeature to use (new data-driven approach)
        /// </summary>
        public TreeFeature? Feature { get; set; }

        /// <summary>
        /// Legacy: Which type of Tree (deprecated, use Feature instead)
        /// </summary>
        [Obsolete("Use Feature property instead. This is for backward compatibility only.")]
        public Type? TreeType { get; set; }

        public TreeInfo(int frequency, TreeFeature feature)
        {
            Frequency = frequency;
            Feature = feature;
        }

        // Legacy constructor for backward compatibility
        [Obsolete("Use constructor with TreeFeature instead")]
        public TreeInfo(int frequency, Type treeType)
        {
            Frequency = frequency;
            TreeType = treeType;
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
