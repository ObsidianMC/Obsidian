namespace Obsidian.API;
/// <summary>What an explosion does to the blocks it reaches.</summary>
public enum BlockInteraction
{
    /// <summary>Leaves blocks in place.</summary>
    Keep,

    /// <summary>Breaks blocks and drops every block's loot.</summary>
    Destroy,

    /// <summary>Breaks blocks and drops loot from only some of them, the way TNT does.</summary>
    DestroyWithDecay,

    /// <summary>
    /// Leaves blocks in place but triggers blocks that react to explosions, such as buttons and bells.
    /// </summary>
    TriggerBlock
}
