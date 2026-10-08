using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.WindowProperties;

namespace Obsidian.Entities;

public partial class Player
{
    internal int EnchantmentSeed { get; private set; } = Globals.Random.Next();
    private readonly int[] enchantmentCosts = new int[3];
    private static readonly IReadOnlyList<EnchantmentDefinition> tableEnchantments = EnchantmentsRegistry.All
        .Where(enchantment => TagsRegistry.Enchantment.InEnchantingTable.Entries.Contains(enchantment.Id)).ToArray();

    private int CountBookshelves(Vector position)
    {
        var terrain = new MobTerrain(Level);
        var count = 0;
        for (var x = -2; x <= 2; x++)
        for (var y = 0; y <= 1; y++)
        for (var z = -2; z <= 2; z++)
        {
            if (Math.Abs(x) != 2 && Math.Abs(z) != 2) continue;
            var shelf = terrain.GetBlock(new Vector(position.X + x, position.Y + y, position.Z + z));
            var gap = terrain.GetBlock(new Vector(position.X + x / 2, position.Y + y, position.Z + z / 2));
            if (shelf != null && gap != null && TagsRegistry.Block.EnchantmentPowerProvider.Entries.Contains(shelf.RegistryId) &&
                TagsRegistry.Block.EnchantmentPowerTransmitter.Entries.Contains(gap.RegistryId)) count++;
        }
        return Math.Min(15, count);
    }

    internal static int EnchantingCost(LegacyRandomSource random, int option, int bookshelves)
    {
        bookshelves = Math.Clamp(bookshelves, 0, 15);
        var power = random.NextInt(8) + 1 + bookshelves / 2 + random.NextInt(bookshelves + 1);
        return option switch { 0 => Math.Max(power / 3, 1), 1 => power * 2 / 3 + 1, _ => Math.Max(power, bookshelves * 2) };
    }

    private List<(EnchantmentDefinition Enchantment, int Level)> EnchantingSelection(ItemStack item, int option, int cost, out LegacyRandomSource random)
    {
        random = new LegacyRandomSource(unchecked(EnchantmentSeed + option));
        var selected = EnchantmentHelper.SelectEnchantments(random, item, cost, tableEnchantments);
        if (item.Type == Material.Book && selected.Count > 1) selected.RemoveAt(random.NextInt(selected.Count));
        return selected;
    }

    internal async ValueTask RefreshEnchantingAsync()
    {
        if (OpenedContainer is not EnchantmentTable table) return;
        var item = table.GetItem(0);
        var random = new LegacyRandomSource(EnchantmentSeed);
        var shelves = CountBookshelves(table.BlockPosition);
        var enchantable = item is { Count: 1 } && !item.RemoveComponents.Contains(DataComponentType.Enchantable) &&
            (item.GetComponent<SimpleDataComponent<int>>(DataComponentType.Enchantable)?.Value ?? item.Holder.Enchantable) > 0 &&
            (item.GetComponent<SimpleDataComponent<Enchantment[]>>(DataComponentType.Enchantments)?.Value?.Length ?? 0) == 0;
        for (var option = 0; option < 3; option++)
        {
            enchantmentCosts[option] = enchantable ? EnchantingCost(random, option, shelves) : 0;
            if (enchantmentCosts[option] < option + 1) enchantmentCosts[option] = 0;
        }
        await SetEnchantingPropertyAsync(3, (short)(EnchantmentSeed & ~15));
        for (var option = 0; option < 3; option++)
        {
            var cost = enchantmentCosts[option];
            var clue = -1;
            var level = -1;
            if (cost > 0)
            {
                var selected = EnchantingSelection(item!, option, cost, out var selectionRandom);
                if (selected.Count > 0)
                {
                    var chosen = selected[selectionRandom.NextInt(selected.Count)];
                    clue = chosen.Enchantment.Id;
                    level = chosen.Level;
                }
            }
            await SetEnchantingPropertyAsync(option, (short)cost);
            await SetEnchantingPropertyAsync(option + 4, (short)clue);
            await SetEnchantingPropertyAsync(option + 7, (short)level);
        }
    }

    private ValueTask SetEnchantingPropertyAsync(int property, short value) => Client.QueuePacketAsync(new ContainerSetDataPacket
    {
        ContainerId = CurrentContainerId,
        WindowProperty = new EnchantmentTableWindowProperty((EnchantmentTableProperty)property, value)
    });

    internal async ValueTask EnchantAsync(int containerId, int option)
    {
        if (containerId != CurrentContainerId || option is < 0 or > 2 || OpenedContainer is not EnchantmentTable table ||
            !Alive || Respawning || GameMode == GameMode.Spectator || (Position - (VectorD)table.BlockPosition).Magnitude > 8 ||
            new MobTerrain(Level).GetBlock(table.BlockPosition)?.Material != Material.EnchantingTable) return;
        await RefreshEnchantingAsync();
        var item = table.GetItem(0);
        var lapis = table.GetItem(1);
        var cost = enchantmentCosts[option];
        var paidLevels = option + 1;
        if (item is not { Count: 1 } || cost <= 0 || GameMode != GameMode.Creative &&
            (XpLevel < cost || XpLevel < paidLevels || lapis is not { Type: Material.LapisLazuli } || lapis.Count < paidLevels)) return;
        var selected = EnchantingSelection(item, option, cost, out _);
        if (selected.Count == 0) return;
        if (item.Type == Material.Book) item = item.TransmuteCopy(ItemsRegistry.EnchantedBook);
        foreach (var (enchantment, level) in selected) EnchantmentHelper.Enchant(item, enchantment, level);
        table.SetItem(0, item);
        if (GameMode != GameMode.Creative)
        {
            table.RemoveItem(1, paidLevels);
            XpLevel -= paidLevels;
            // Enchanting deducts levels, preserving progress and total experience as vanilla does.
            await Client.QueuePacketAsync(new SetExperiencePacket(XpP, XpLevel, XpTotal));
        }
        EnchantmentSeed = Globals.Random.Next();
        await Client.QueuePacketAsync(new ContainerSetContentPacket(CurrentContainerId,
            table.Concat(Inventory.Skip(9).Take(36)).ToList()) { CarriedItem = CarriedItem });
        await RefreshEnchantingAsync();
    }
}
