using Obsidian.API.Inventory;

namespace Obsidian.API;

/// <summary>What the client shows when a player hovers over a chat component.</summary>
public sealed record class HoverComponent
{
    /// <summary>Gets or sets the kind of tooltip to show.</summary>
    public required HoverAction Action { get; set; }

    /// <summary>Gets or sets what the tooltip shows; its type must match <see cref="Action"/>.</summary>
    public required IHoverContent Contents { get; set; }
}

/// <summary>Hover content that shows a text component, for <see cref="HoverAction.ShowText"/>.</summary>
public sealed record class HoverChatContent : IHoverContent
{
    /// <summary>Gets or sets the text to show.</summary>
    public required ChatMessage ChatMessage { get; set; }
}

/// <summary>Hover content that shows an item's tooltip, for <see cref="HoverAction.ShowItem"/>.</summary>
public sealed record class HoverItemContent : IHoverContent
{
    /// <summary>Gets or sets the item whose tooltip is shown.</summary>
    public required ItemStack Item { get; set; }
}

/// <summary>
/// Hover content for <see cref="HoverAction.ShowEntity"/>, which would show an entity's name, type and UUID.
/// The current NBT serializers can't write it: sending or storing a component that holds it throws
/// <see cref="NotSupportedException"/>.
/// </summary>
public sealed record class HoverEntityComponent : IHoverContent
{
    /// <summary>Gets or sets the entity to describe.</summary>
    public required IEntity Entity { get; set; }
}

/// <summary>Marks a type as the content of a <see cref="HoverComponent"/>.</summary>
public interface IHoverContent { }
