using System;

namespace Obsidian.API.Containers
{
    public sealed class CartographyTable : ResultContainer
    {
        public CartographyTable() : base(3, InventoryType.CartographyTable)
        {
            this.Title = "Cartography Table";
        }

        public override ItemStack? GetResult() => throw new NotImplementedException();
        public override void SetResult(ItemStack? result) => throw new NotImplementedException();
    }
}
