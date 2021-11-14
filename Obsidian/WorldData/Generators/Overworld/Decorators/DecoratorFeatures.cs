using Obsidian.WorldData.Generators.Overworld.Features.Trees;
using System;
using System.Collections.Generic;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

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

    public class TreeInfo
    {
        /// <summary>
        /// Density of trees in the biome.
        /// 0 for none.
        /// 10 for a lot.
        /// </summary>
        public int Frequency { get; set; } = 0;

        /// <summary>
        /// Which type of Tree
        /// </summary>
        public Type TreeType { get; set; } = typeof(BaseTree);

        public TreeInfo(int frequency, Type treeType)
        {
            Frequency = frequency;
            TreeType = treeType;
        }
    }

    public class FloraInfo
    {
        public int Frequency { get; set; }

        public Type FloraType { get; set; }

        public int Radius { get; set; }

        public int Density { get; set; }

        public FloraInfo(int frequency, Type floraType, int radius, int density)
        {
            Frequency = frequency;
            Radius = radius;
            Density = density;
            FloraType = floraType;
        }
    }
}
