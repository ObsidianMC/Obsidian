using Obsidian.API.Inventory;

namespace Obsidian.API;

public sealed class HoverComponent
{
    public required HoverAction Action { get; set; }

    public required IHoverContent Contents { get; set; }
}

public sealed class HoverChatContent : IHoverContent
{
    public required ChatMessage ChatMessage { get; set; }
}

public sealed class HoverItemContent : IHoverContent
{
    public required ItemStack Item { get; set; }
}

public sealed class HoverEntityComponent : IHoverContent
{
    public required IEntity Entity { get; set; }
}

public interface IHoverContent { }
