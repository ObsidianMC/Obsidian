﻿using System;
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

        /// <inheritdoc/>
        public IEnumerator<ItemStack> GetEnumerator() => new IngredientEnumerator(this.items);

        IEnumerator IEnumerable.GetEnumerator() => (IEnumerator)this;

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
}
