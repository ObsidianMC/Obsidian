using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.Nbt;
using Obsidian.Net;

namespace Obsidian.WorldData;

/// <summary>Four independent cooking items; vanilla's CookingTimes and CookingTotalTimes are int arrays.</summary>
internal sealed class CampfireBlockEntity()
    : VanillaContainerBlockEntity(new CookingItems(), "CookingTimes", "CookingTotalTimes")
{
    private sealed class CookingItems() : BaseContainer(4);

    private readonly int[] spent = new int[4];
    private readonly int[] total = new int[4];

    // Vanilla campfires don't expose their cooking items to hoppers.
    public override IItemAccess? GetItemAccess(BlockFace? side) => null;

    protected internal override void Load(NbtCompound tag)
    {
        base.Load(tag);
        Read("CookingTimes", this.spent);
        Read("CookingTotalTimes", this.total);

        void Read(string name, int[] target)
        {
            if (tag.TryGetTag<NbtArray<int>>(name, out var saved))
                saved.GetArray().AsSpan(0, Math.Min(saved.Count, target.Length)).CopyTo(target);
        }
    }

    internal override void SaveMachineState(NbtCompound tag)
    {
        tag.Add(new NbtArray<int>("CookingTimes", [.. this.spent]));
        tag.Add(new NbtArray<int>("CookingTotalTimes", [.. this.total]));
    }

    protected internal override NbtCompound? GetUpdateData()
    {
        var tag = new NbtCompound();
        var items = new NbtList(NbtTagType.Compound, "Items");
        using (this.Inventory.Lock())
        {
            for (var slot = 0; slot < 4; slot++)
            {
                if (this.Inventory[slot] is not ItemStack stack || stack.IsAir)
                    continue;

                var saved = stack.ToNbt();
                saved.Add(new NbtTag<byte>("Slot", (byte)slot));
                items.Add(saved);
            }
        }

        tag.Add(items);
        return tag;
    }

    /// <summary>Called under the inventory lock; inputs are copied and only one item occupies a slot.</summary>
    internal bool Place(ItemStack input, int cookingTime)
    {
        for (var slot = 0; slot < 4; slot++)
        {
            if (!this.Inventory[slot].IsNullOrAir())
                continue;

            this.Inventory[slot] = NetworkBuffer.SnapshotItemStack(new ItemStack(input, 1));
            this.spent[slot] = 0;
            this.total[slot] = cookingTime;
            this.SetChanged();
            return true;
        }

        return false;
    }

    internal IReadOnlyList<ItemStack> Cook(RecipeSet recipes, bool lit)
    {
        List<ItemStack>? finished = null;
        for (var slot = 0; slot < 4; slot++)
        {
            if (!lit)
            {
                if (this.spent[slot] > 0)
                {
                    this.spent[slot] = Math.Max(0, this.spent[slot] - 2);
                    this.SetChanged();
                }

                continue;
            }

            if (this.Inventory[slot] is not ItemStack input || input.IsAir || input.Count <= 0)
                continue;

            this.spent[slot]++;
            this.SetChanged();
            if (this.spent[slot] < this.total[slot])
                continue;

            // Vanilla checks the current recipe at completion; removal during a reload drops the input instead.
            var recipe = recipes.FindCooking(CraftingType.CampfireCooking, input);
            (finished ??= []).Add(NetworkBuffer.SnapshotItemStack(recipe?.Result.FirstOrDefault() ?? input));
            this.Inventory[slot] = null;
        }

        return finished is not null ? finished : Array.Empty<ItemStack>();
    }

    protected internal override ValueTask TickAsync() =>
        this.Level is AbstractLevel level ? level.TickCampfireAsync(this) : ValueTask.CompletedTask;
}
