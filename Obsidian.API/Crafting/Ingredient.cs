using Obsidian.API.Inventory;
using System.Collections;

namespace Obsidian.API.Crafting;

/// <summary>
/// The stacks one recipe slot accepts, any one of which matches (vanilla's ingredient: an item, a list of items or an
/// item tag).
/// </summary>
public class Ingredient : IEnumerable<ItemStack>
{
    private readonly List<ItemStack> items;

    /// <summary>
    /// Gets how many stacks were added to the ingredient, duplicates included.
    /// </summary>
    public int Count => this.items.Count;

    /// <summary>
    /// Creates an ingredient that accepts nothing until stacks are added.
    /// </summary>
    public Ingredient()
    {
        this.items = [];
    }

    /// <summary>
    /// Adds <paramref name="item"/> to the stacks the ingredient accepts.
    /// </summary>
    public void Add(ItemStack item) => this.items.Add(item);

    /// <summary>
    /// Removes <paramref name="item"/> from the stacks the ingredient accepts, if it's there.
    /// </summary>
    public void Remove(ItemStack item) => this.items.Remove(item);

    /// <summary>
    /// Returns the accepted stacks in the order they were added.
    /// </summary>
    /// <remarks>
    /// Only this generic enumerator works: the non-generic <see cref="IEnumerable.GetEnumerator"/> casts the
    /// ingredient itself to <see cref="IEnumerator"/> and throws <see cref="InvalidCastException"/>, so non-generic
    /// enumeration of an ingredient fails.
    /// </remarks>
    public IEnumerator<ItemStack> GetEnumerator() => new IngredientEnumerator(this.items);

    IEnumerator IEnumerable.GetEnumerator() => (IEnumerator)this;

    /// <summary>
    /// Whether <paramref name="item"/> matches one of the accepted stacks: the same item with the same components,
    /// whatever its count.
    /// </summary>
    public bool CanBe(ItemStack item) => this.items.Any(x => x.Equals(item));

    private class IngredientEnumerator : IEnumerator<ItemStack>
    {
        public int Position { get; set; } = -1;

        private List<ItemStack> items;

        public ItemStack Current
        {
            get
            {
                return (Position >= 0 && Position < items.Count) ? items[Position] : throw new InvalidOperationException();
            }
        }

        object IEnumerator.Current
        {
            get
            {
                return (Position >= 0 && Position < items.Count) ? items[Position] : throw new InvalidOperationException();
            }
        }

        public IngredientEnumerator(List<ItemStack> items)
        {
            this.items = items;
        }

        public bool MoveNext()
        {
            return ++Position < items.Count;
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }
    }
}
