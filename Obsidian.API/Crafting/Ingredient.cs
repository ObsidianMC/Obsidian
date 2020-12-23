using System;
using System.Collections;
using System.Collections.Generic;

namespace Obsidian.API.Crafting
{
    public class Ingredient : IEnumerable<ItemStack>
    {
        private readonly List<ItemStack> items;

        public int Count => this.items.Count;

        public Ingredient()
        {
            this.items = new List<ItemStack>();
        }

        public void Add(ItemStack item) => this.items.Add(item);

        public void Remove(ItemStack item) => this.items.Remove(item);

        public IEnumerator<ItemStack> GetEnumerator() => new IngredientEnumerator(this.items);

        IEnumerator IEnumerable.GetEnumerator() => (IEnumerator)this;

        private class IngredientEnumerator : IEnumerator<ItemStack>
        {
            private List<ItemStack> items;

            public int Position { get; set; } = -1;

            public ItemStack Current
            {
                get
                {
                    try
                    {
                        return this.items[this.Position];
                    }
                    catch (IndexOutOfRangeException)
                    {
                        throw new InvalidOperationException();
                    }
                }
            }

            object IEnumerator.Current
            {
                get
                {
                    try
                    {
                        return this.items[this.Position];
                    }
                    catch (IndexOutOfRangeException)
                    {
                        throw new InvalidOperationException();
                    }
                }
            }

            public IngredientEnumerator(List<ItemStack> items)
            {
                this.items = items;
            }

            public void Dispose()
            {
                this.items = null;
            }

            public bool MoveNext()
            {
                this.Position++;

                return this.Position < this.items.Count;
            }

            public void Reset() => this.Position = -1;
        }
    }
}
