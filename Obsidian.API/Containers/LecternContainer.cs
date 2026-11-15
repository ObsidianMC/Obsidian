using Obsidian.API.Inventory;

namespace Obsidian.API.Containers;

/// <summary>
/// A lectern's book, which its window shows a page of, like vanilla's <c>LecternMenu</c>.
/// </summary>
public sealed class LecternContainer : BaseContainer
{
    /// <summary>
    /// The book slot's index.
    /// </summary>
    public const int BookSlot = 0;

    /// <summary>
    /// Gets or sets the position of the lectern.
    /// </summary>
    public Vector BlockPosition { get; set; }

    /// <summary>
    /// The page the book is open at.
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Creates a lectern's empty book slot.
    /// </summary>
    public LecternContainer() : base(1, InventoryType.Lectern)
    {
        this.Title = "Lectern";
    }

    /// <summary>
    /// The book on the lectern, if any.
    /// </summary>
    public ItemStack? Book => this.GetItem(BookSlot) is { Count: > 0 } book ? book : null;
}
