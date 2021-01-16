namespace Obsidian.API
{
    public enum EHoverAction
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
}
