namespace Obsidian.API.Blocks;

/// <summary>The area of a block face required for support, matching vanilla's support tests.</summary>
public enum SupportType
{
    /// <summary>The entire unit face.</summary>
    Full,

    /// <summary>The central 2/16 by 2/16 square.</summary>
    Center,

    /// <summary>The outer 2/16-wide rim.</summary>
    Rigid
}
