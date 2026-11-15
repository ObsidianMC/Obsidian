namespace Obsidian.API;

/// <summary>What the client shows when a player hovers over a piece of chat text.</summary>
public enum HoverAction
{
    /// <summary>
    /// Shows a raw JSON text component.
    /// </summary>
    ShowText,
    /// <summary>
    /// Shows the tooltip of an item as if it was being hovering over it in an inventory.
    /// </summary>
    ShowItem,
    /// <summary>
    /// Shows an entity's name, type, and UUID. 
    /// </summary>
    ShowEntity
}
