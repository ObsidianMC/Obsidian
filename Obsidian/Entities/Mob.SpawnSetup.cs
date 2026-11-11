using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData;

namespace Obsidian.Entities;

public partial class Mob
{
    // Vanilla's EquipmentSlot.VALUES order of the armour slots, which populateDefaultEquipmentEnchantments follows.
    private static readonly EquipmentSlot[] enchantedArmorSlots =
        [EquipmentSlot.Boots, EquipmentSlot.Leggings, EquipmentSlot.Chestplate, EquipmentSlot.Helmet];

    // The on_mob_spawn_equipment tag's enchantments, in the tag's order, which the weighted picks depend on.
    private static readonly Lazy<EnchantmentDefinition[]> spawnEquipmentEnchantments = new(() =>
        TagsRegistry.Enchantment.OnMobSpawnEquipment.Entries
            .Select(id => EnchantmentsRegistry.All.First(enchantment => enchantment.Id == id))
            .ToArray());

    /// <summary>
    /// The spawn setup a spawner gives a mob from spawn data that only names it, like vanilla's <c>finalizeSpawn</c>
    /// with the level's random. Types whose setup draws from the level's random in vanilla's order override it; the
    /// others set themselves up from their own random (see <see cref="FinalizeSpawn"/>).
    /// </summary>
    internal virtual void FinalizeBareSpawn(AbstractLevel level, IRandomSource random) => this.FinalizeSpawn();

    /// <summary>
    /// Vanilla's <c>populateDefaultEquipmentEnchantments</c> from the level's random: the main hand's chance, then
    /// each worn armour piece's, in vanilla's slot order. An empty slot draws nothing.
    /// </summary>
    protected void PopulateEquipmentEnchantmentsFromLevel(IRandomSource random, float special)
    {
        this.EnchantSpawnedEquipmentFromLevel(random, special);
        foreach (var slot in enchantedArmorSlots)
            this.TryEnchantSpawnedEquipment(slot, random, 0.5f, special);
    }

    /// <summary>
    /// Vanilla's <c>enchantSpawnedWeapon</c>: the main hand's one in four, by the special multiplier.
    /// </summary>
    protected void EnchantSpawnedEquipmentFromLevel(IRandomSource random, float special) =>
        this.TryEnchantSpawnedEquipment(EquipmentSlot.MainHand, random, 0.25f, special);

    // Vanilla's enchantSpawnedEquipment through the mob_spawn_equipment provider (EnchantmentsByCostWithDifficulty,
    // costs 5 to 5 + 17 x the special multiplier): a cost drawn between them, then the enchantments vanilla's
    // selectEnchantment picks for it from the on_mob_spawn_equipment tag, in the tag's order.
    private void TryEnchantSpawnedEquipment(EquipmentSlot slot, IRandomSource random, float chance, float special)
    {
        const int MinimumCost = 5;
        const int CostSpan = 17;

        var item = GetEquipment(slot);
        if (item.IsAir || random.NextFloat() >= chance * special)
            return;

        var maximumCost = MinimumCost + (int)(special * CostSpan);
        var cost = random.NextInt(maximumCost - MinimumCost + 1) + MinimumCost;
        var enchantments = EnchantmentHelper.SelectEnchantments(random, item, cost, spawnEquipmentEnchantments.Value);
        foreach (var (enchantment, enchantmentLevel) in enchantments)
            EnchantmentHelper.Enchant(item, enchantment, enchantmentLevel);

        SetEquipment(slot, item);
    }
}
