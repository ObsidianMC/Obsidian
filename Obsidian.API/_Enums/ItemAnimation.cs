namespace Obsidian.API;
/// <summary>The animation a player plays while using an item.</summary>
public enum ItemAnimation
{
    /// <summary>No animation.</summary>
    None,
    /// <summary>The eating animation.</summary>
    Eat,
    /// <summary>The drinking animation.</summary>
    Drink,
    /// <summary>The shield-blocking animation.</summary>
    Block,
    /// <summary>Drawing a bow.</summary>
    Bow,
    /// <summary>Raising the item overhead, as with a trident.</summary>
    Spear,
    /// <summary>Loading a crossbow.</summary>
    Crossbow,
    /// <summary>Looking through a spyglass.</summary>
    Spyglass,
    /// <summary>Blowing a goat horn.</summary>
    TootHorn,
    /// <summary>Brushing with a brush.</summary>
    Brush,
    /// <summary>Picking an item out of a bundle.</summary>
    Bundle
}
