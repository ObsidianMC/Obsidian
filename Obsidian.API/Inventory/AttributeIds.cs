namespace Obsidian.API.Inventory;

/// <summary>
/// The ids of attributes that items' attribute modifiers change, for <see cref="AttributeModifier.Id"/>.
/// </summary>
/// <remarks>
/// They're the attributes' protocol ids, their positions in vanilla's attribute registry, which clients read
/// the modifiers with. A player gets the modifiers of what it holds and wears, for every attribute it has.
/// </remarks>
public static partial class AttributeIds
{
}
