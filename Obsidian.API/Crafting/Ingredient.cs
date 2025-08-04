using Obsidian.API.Inventory;
using System.Collections;

namespace Obsidian.API.Crafting;

public class Ingredient : IEnumerable<ItemStack>
{
    private readonly List<ItemStack> items;

    public int Count => this.items.Count;

    public Ingredient()
    {
        this.items = [];
    }

    public void Add(ItemStack item) => this.items.Add(item);

    public void Remove(ItemStack item) => this.items.Remove(item);

    public IEnumerator<ItemStack> GetEnumerator() => new IngredientEnumerator(this.items);

    IEnumerator IEnumerable.GetEnumerator() => (IEnumerator)this;

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
