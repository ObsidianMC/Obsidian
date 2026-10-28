using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.Nbt;

namespace Obsidian.Builtins.WorldEdit;

internal static class WorldEditTools
{
    private const string ToolKey = "obsidian:worldedit_tool";

    public static ItemStack CreateWand() => new(ItemsRegistry.WoodenAxe, 1,
        ComponentBuilder.CustomData with { Value = new NbtCompound { new NbtTag<string>(ToolKey, "wand") } },
        ComponentBuilder.CustomName with { Value = ChatMessage.Simple("WorldEdit selection wand") });

    public static bool IsWand(ItemStack? item) =>
        item is { Count: > 0 } && item.Is(ItemsRegistry.WoodenAxe)
        && item.GetComponent<SimpleDataComponent<NbtCompound>>(DataComponentType.CustomData)?.Value is { } data
        && data.TryGetTag<NbtTag<string>>(ToolKey, out var marker) && marker.Value == "wand";

    public static async Task<string> GiveWandAsync(IPlayer player)
    {
        // AddItem merges by item type alone. Choose an empty slot so the tool's marker is never merged into an axe.
        for (var slot = 36; slot <= 44; slot++)
        {
            if (TryGive(player, slot))
            {
                await player.SyncInventorySlotAsync(slot);
                return "Selection wand given. Left-click for position 1, right-click for position 2.";
            }
        }
        for (var slot = 9; slot < 36; slot++)
        {
            if (TryGive(player, slot))
            {
                await player.SyncInventorySlotAsync(slot);
                return "Selection wand given. Left-click for position 1, right-click for position 2.";
            }
        }
        throw new ArgumentException("Your inventory is full. Free a slot first.");
    }

    private static bool TryGive(IPlayer player, int slot)
    {
        if (player.Inventory.GetItem(slot) is { IsAir: false, Count: > 0 })
            return false;
        player.Inventory.SetItem(slot, CreateWand());
        return true;
    }
}
