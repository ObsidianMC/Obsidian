using Obsidian.API.Inventory;

namespace Obsidian.WorldData;

/// <summary>1.21.11 FuelValues.vanillaBurnTimes; vanilla datagen supplies no fuel durations.</summary>
internal static class FurnaceFuels
{
    private static readonly (Tag Tag, int Ticks)[] tagged =
    [
        (TagsRegistry.Item.Logs, 300),
        (TagsRegistry.Item.BambooBlocks, 300),
        (TagsRegistry.Item.Planks, 300),
        (TagsRegistry.Item.WoodenStairs, 300),
        (TagsRegistry.Item.WoodenSlabs, 150),
        (TagsRegistry.Item.WoodenTrapdoors, 300),
        (TagsRegistry.Item.WoodenPressurePlates, 300),
        (TagsRegistry.Item.WoodenShelves, 300),
        (TagsRegistry.Item.WoodenFences, 300),
        (TagsRegistry.Item.FenceGates, 300),
        (TagsRegistry.Item.Banners, 300),
        (TagsRegistry.Item.Signs, 200),
        (TagsRegistry.Item.HangingSigns, 800),
        (TagsRegistry.Item.WoodenDoors, 200),
        (TagsRegistry.Item.Boats, 1200),
        (TagsRegistry.Item.Wool, 100),
        (TagsRegistry.Item.WoodenButtons, 100),
        (TagsRegistry.Item.Saplings, 100),
        (TagsRegistry.Item.WoolCarpets, 67)
    ];

    /// <summary>Returns fuel ticks; a smoker or blast furnace halves this duration.</summary>
    internal static int BurnTime(ItemStack item)
    {
        if (item.IsAir || item.Count <= 0 || TagsRegistry.Item.NonFlammableWood.Entries.Contains(item.Holder.Id))
            return 0;

        var duration = item.Holder.Id switch
        {
            ItemIds.LavaBucket => 20000,
            ItemIds.CoalBlock => 16000,
            ItemIds.BlazeRod => 2400,
            ItemIds.Coal or ItemIds.Charcoal => 1600,
            // FuelValues adds one tick after multiplying/dividing for kelp and carpets.
            ItemIds.DriedKelpBlock => 4001,
            ItemIds.BambooMosaicSlab => 150,
            ItemIds.WoodenShovel or ItemIds.WoodenSword or ItemIds.WoodenSpear or ItemIds.WoodenHoe
                or ItemIds.WoodenAxe or ItemIds.WoodenPickaxe => 200,
            ItemIds.Bamboo or ItemIds.Scaffolding => 50,
            ItemIds.Stick or ItemIds.Bowl or ItemIds.DeadBush or ItemIds.ShortDryGrass or ItemIds.TallDryGrass
                or ItemIds.Azalea or ItemIds.FloweringAzalea or ItemIds.LeafLitter => 100,
            ItemIds.BambooMosaic or ItemIds.BambooMosaicStairs or ItemIds.NoteBlock or ItemIds.Bookshelf
                or ItemIds.ChiseledBookshelf or ItemIds.Lectern or ItemIds.Jukebox or ItemIds.Chest
                or ItemIds.TrappedChest or ItemIds.CraftingTable or ItemIds.DaylightDetector or ItemIds.Bow
                or ItemIds.FishingRod or ItemIds.Ladder or ItemIds.Crossbow or ItemIds.Loom or ItemIds.Barrel
                or ItemIds.CartographyTable or ItemIds.FletchingTable or ItemIds.SmithingTable or ItemIds.Composter
                or ItemIds.MangroveRoots => 300,
            _ => 0
        };

        foreach (var (tag, ticks) in tagged)
        {
            if (tag.Entries.Contains(item.Holder.Id))
                duration = ticks;
        }

        return duration;
    }
}
