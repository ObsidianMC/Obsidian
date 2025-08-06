using Obsidian.API.Inventory;

namespace Obsidian.API;

public sealed record class HoverComponent
{
    public required HoverAction Action { get; set; }

    public required IHoverContent Contents { get; set; }
}

public sealed record class HoverChatContent : IHoverContent
{
    public required ChatMessage ChatMessage { get; set; }
}

public sealed record class HoverItemContent : IHoverContent
{
    public required ItemStack Item { get; set; }
}

public sealed record class HoverEntityComponent : IHoverContent
{
    public required IEntity Entity { get; set; }
}

public interface IHoverContent { }
