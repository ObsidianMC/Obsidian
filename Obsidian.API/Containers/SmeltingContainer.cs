using System;

namespace Obsidian.API.Containers
{
    public sealed class SmeltingContainer : AbstractSmeltingContainer, ITileEntity
    {
        public string Id { get; }

        public Vector? BlockPosition { get; set; }

        public SmeltingContainer(InventoryType type, string id) : base(3, type)
        {
            if (type != InventoryType.Furnace || type != InventoryType.Smoker || type != InventoryType.BlastFurnace || type != InventoryType.Custom)
                throw new InvalidOperationException("Type must either be custom, furnace, blast furnace or smoker.");

            this.Id = id;
        }

        public void ToNbt() => throw new NotImplementedException();
        public void FromNbt() => throw new NotImplementedException();
        public override ItemStack? GetFuel() => throw new NotImplementedException();
        public override ItemStack? GetIngredient() => throw new NotImplementedException();
        public override void SetResult(ItemStack? result) => throw new NotImplementedException();
        public override ItemStack? GetResult() => throw new NotImplementedException();
    }
}
