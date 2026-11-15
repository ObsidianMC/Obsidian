namespace Obsidian.API;
/// <summary>The recipe book tab a crafting recipe is listed under.</summary>
public enum CraftingBookCategory : int
{
    /// <summary>Building blocks.</summary>
    Building,
    /// <summary>Redstone components.</summary>
    Redstone,
    /// <summary>Tools, weapons and armor.</summary>
    Equipment,
    /// <summary>Anything else.</summary>
    Misc
}
