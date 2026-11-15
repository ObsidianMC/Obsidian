namespace Obsidian.API;

/// <summary>The two kinds of light a chunk stores.</summary>
public enum LightType : int
{
    /// <summary>Light from the sky.</summary>
    Sky,
    /// <summary>Light given off by blocks such as torches.</summary>
    Block
}
