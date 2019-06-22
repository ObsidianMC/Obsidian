using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.Util.DataTypes
{
    public enum BlockFace : sbyte
    {
        /// <summary>
        /// -Y
        /// </summary>
        Bottom = 0,
        /// <summary>
        /// +Y
        /// </summary>
        Top = 1,
        /// <summary>
        /// -Z
        /// </summary>
        North = 2,
        /// <summary>
        /// +Z
        /// </summary>
        South = 3,
        /// <summary>
        /// -X
        /// </summary>
        West = 4,
        /// <summary>
        /// +X
        /// </summary>
        East = 5
    }
}
