using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// A lectern's book, which its window shows a page of, like vanilla's <c>LecternMenu</c>.
/// </summary>
public sealed class LecternContainer : BaseContainer
{
    public const int BookSlot = 0;

    public Vector BlockPosition { get; set; }

    /// <summary>
    /// The page the book is open at.
    /// </summary>
    public int Page { get; set; }

    public LecternContainer() : base(1, InventoryType.Lectern)
    {
        this.Title = "Lectern";
    }

    /// <summary>
    /// The book on the lectern, if any.
    /// </summary>
    public ItemStack? Book => this.GetItem(BookSlot) is { Count: > 0 } book ? book : null;
}
