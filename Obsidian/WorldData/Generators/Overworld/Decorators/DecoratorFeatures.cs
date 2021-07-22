using Obsidian.WorldData.Generators.Overworld.Features.Trees;
using System;
using System.Collections.Generic;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class DecoratorFeatures
    {
        /// <summary>
        /// List of Tree types and frequency of each.
        /// </summary>
        public List<TreeInfo> Trees { get; set; } = new List<TreeInfo>();

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
    }
}
