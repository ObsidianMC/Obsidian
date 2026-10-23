using System.Collections.Immutable;
using Obsidian.API;
using Obsidian.API.Effects;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using Obsidian.Entities;
using Obsidian.Entities.Factories;
using Obsidian.Tests.Fakes;
using Obsidian.Registries;
using Obsidian.API.World.Generator.RandomSources;
using System.Text.Json;
using System.Threading.Tasks;

namespace Obsidian.Tests;

/// <summary>
/// Parity checks for container loot. Expected slots come from vanilla 1.21.11's <c>LootTable.fill</c> into an empty
/// 27-slot container with the context of a player opening a chest at (0, 64, 0) in a world with seed 12345.
/// </summary>
[Collection(nameof(PluginTagFiles))]
public class VanillaLoot
{
    private const long WorldSeed = 12345L;

    [Theory]
    // Each non-empty slot as "slot:item*count" plus its components.
    [InlineData("minecraft:chests/simple_dungeon", 1L,
        "0:gunpowder*1 2:string*3 3:gunpowder*1 5:string*1 8:bone*1 9:string*2 10:gunpowder*1 12:bucket*1 20:gunpowder*3 22:golden_apple*1 25:iron_ingot*1 26:bone*1")]
    [InlineData("minecraft:chests/shipwreck_map", -1L,
        "8:book*1 11:book*2 12:map*1,item_name=filled_map.buried_treasure 13:book*2 14:paper*1 16:paper*8 24:book*1")]
    [InlineData("minecraft:archaeology/desert_well", 5086654115216342560L, "8:suspicious_stew*1,stew=17:140")]
    [InlineData("minecraft:chests/trial_chambers/reward_ominous", -6169532649852302182L,
        "2:emerald*1 4:ominous_bottle*1,ominous=2 6:diamond*1 8:emerald*5 9:diamond*1 10:iron_block*1 13:music_disc_creator*1 15:emerald*1")]
    [InlineData("minecraft:chests/trial_chambers/reward_ominous_rare", -7912908803613548926L, "9:enchanted_book*1,stored=wind_burst:1")]
    // Seed 0 draws from the table's random sequence, seeded from the world seed.
    [InlineData("minecraft:chests/simple_dungeon", 0L,
        "1:iron_ingot*1 3:enchanted_book*1,stored=blast_protection:1 6:coal*1 7:bone*1 8:iron_ingot*1 10:gunpowder*2 11:bucket*1 12:gunpowder*1 13:coal*1 14:music_disc_13*1 17:bone*2 18:gunpowder*1 19:gunpowder*1 21:iron_ingot*1 23:enchanted_golden_apple*1 26:bone*1")]
    public void FillMatchesVanilla(string table, long seed, string expected) => Assert.Equal(expected, Fill(table, seed));

    [Theory]
    // SHA-256 of the slots as above; covers enchanting with levels, damaged gear and goat horn instruments.
    [InlineData("minecraft:chests/ancient_city", -1L, "1380cc04902567a533c24c0eb8069852ff517656a194b5509367d930a534d07a")]
    [InlineData("minecraft:chests/bastion_treasure", 1L, "bce6e34c1bbab99382d1a13e72f9ec10391c607d7c942e0bb9ff247a58b9d9ac")]
    [InlineData("minecraft:chests/pillager_outpost", 1L, "36cd0ccc8c253b03e5291b7c48c1eb4ab8508e0f262f2d68d5ee9dd4baef3d6d")]
    public void FillHashMatchesVanilla(string table, long seed, string expectedHash) =>
        Assert.Equal(expectedHash, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Fill(table, seed)))));

    [Fact]
    public async Task EntityTablesMatchVanillaDrawsAndSmeltThroughCookingRecipes()
    {
        await using var server = TestServerFactory.CreateServer($"entity-loot-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(server.World);
        server.World.RecipeSource = () => server.Server.RecipeRegistry.Current;
        var player = TestServerFactory.CreateConnectedPlayer(server.Server, server.World, "Loot");
        using var stream = typeof(VanillaLoot).Assembly.GetManifestResourceStream(
            "Obsidian.Tests.Assets.entity_loot.json")!;
        using var captures = JsonDocument.Parse(stream);
        foreach (var row in captures.RootElement.EnumerateArray())
        {
            var type = Enum.Parse<EntityType>(row.GetProperty("type").GetString()!, true);
            var mob = EntitySpawner.CreateMob(server.World, type)!;
            mob.InitializeAi(false);
            var byPlayer = row.GetProperty("player").GetBoolean();
            if (byPlayer)
                await mob.DamageCombatAsync(player, 1, CombatDamageKind.Melee);

            var table = LootTables.All[$"minecraft:entities/{row.GetProperty("type").GetString()}"];
            var random = table.CreateRandom(row.GetProperty("seed").GetInt64());
            var context = mob.CreateEntityLootContext(table, byPlayer ? player : null, null, random: random);
            var drops = table.GetRandomItems(context);
            Assert.Equal(row.GetProperty("drops").EnumerateArray().Select(value => value.GetString()),
                drops.Where(stack => stack.Count > 0)
                    .Select(stack => $"{stack.Holder.UnlocalizedName}*{stack.Count}"));
            Assert.Equal(row.GetProperty("nextInt").GetInt32(), random.NextInt());
        }

        var pig = new Pig { Level = server.World };
        pig.InitializeAi(false);
        var pigTable = LootTables.All["minecraft:entities/pig"];
        pig.Ignite(5);
        var cooked = Assert.Single(pigTable.GetRandomItems(pig.CreateEntityLootContext(
            pigTable, null, null, random: pigTable.CreateRandom(42))));
        Assert.Equal(ItemsRegistry.CookedPorkchop, cooked.Holder);
        Assert.Equal(3, cooked.Count);

        // The same furnace function also honours the direct attacker's smelts_loot enchantment tag.
        var sword = ItemsRegistry.GetSingleItem(ItemsRegistry.IronSword);
        EnchantmentHelper.Enchant(sword, EnchantmentsRegistry.FireAspect, 1);
        player.Inventory.SetItem(player.CurrentHeldItemSlot, sword);
        pig.FireTicks = 0;
        cooked = Assert.Single(pigTable.GetRandomItems(pig.CreateEntityLootContext(
            pigTable, player, player, random: pigTable.CreateRandom(42))));
        Assert.Equal(ItemsRegistry.CookedPorkchop, cooked.Holder);
        Assert.Equal(3, cooked.Count);

        // A vanilla mob uses a data pack override through the same path as a plugin mob.
        PluginLootTables.Apply([LootTableJson.Parse("""
            { "pools": [{ "rolls": 1, "entries": [
              { "type": "minecraft:item", "name": "minecraft:diamond" }
            ] }] }
            """, "minecraft:entities/pig")]);
        try
        {
            await pig.KillAsync(player);
            var drop = Assert.Single(server.World.GetEntitiesInRange(pig.Position, 3).OfType<ItemEntity>());
            Assert.Equal(ItemsRegistry.Diamond, drop.Item.Holder);
        }
        finally
        {
            PluginLootTables.Apply([]);
        }
    }

    private static string Fill(string tableId, long seed)
    {
        var table = LootTables.All[tableId];
        var container = new Container();
        table.Fill(container, new LootContext
        {
            Random = table.CreateRandom(seed, new RandomSequences(WorldSeed)),
            Origin = new VectorF(0.5f, 64.5f, 0.5f)
        });

        var slots = new List<string>();
        for (var slot = 0; slot < container.Size; slot++)
        {
            var stack = container.GetItem(slot);
            if (stack is not null)
                slots.Add($"{slot}:{Describe(stack)}");
        }

        return string.Join(" ", slots);
    }

    private static string Describe(ItemStack stack)
    {
        var builder = new StringBuilder($"{Strip(stack.Holder.UnlocalizedName)}*{stack.Count}");
        if (stack.Damage != 0)
            builder.Append($",damage={stack.Damage}");

        var enchantments = stack.GetComponent<SimpleDataComponent<ImmutableArray<Enchantment>>>(
            DataComponentType.Enchantments)?.Value;
        if (enchantments is not null && enchantments.Value.Length > 0)
            builder.Append($",enchantments={DescribeEnchantments(enchantments.Value.ToArray())}");

        var stored = stack.GetComponent<SimpleDataComponent<ImmutableArray<Enchantment>>>(
            DataComponentType.StoredEnchantments)?.Value;
        if (stored is not null && stored.Value.Length > 0)
            builder.Append($",stored={DescribeEnchantments(stored.Value.ToArray())}");

        var potion = stack.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents)?.Potion;
        if (potion is not null)
            builder.Append($",potion={potion.Value.ToString().ToSnakeCase()}");

        var stew = stack.GetComponent<SimpleDataComponent<ImmutableArray<SuspiciousStewEffect>>>(
            DataComponentType.SuspiciousStewEffects)?.Value;
        if (stew is not null)
            builder.Append(
                $",stew={string.Join("/", stew.Value.Select(effect => $"{effect.EffectId}:{effect.Duration}"))}");

        if (stack.ItemName is not null)
            builder.Append($",item_name={stack.ItemName.Translate}");

        var instrument = stack.GetComponent<SimpleDataComponent<InstrumentData>>(DataComponentType.Instrument)?.Value;
        if (instrument is not null)
            builder.Append($",instrument={instrument.Description.Translate}");

        var amplifier = stack.GetComponent<SimpleDataComponent<int>>(DataComponentType.OminousBottleAmplifier)?.Value ?? 0;
        if (amplifier != 0)
            builder.Append($",ominous={amplifier}");

        return builder.ToString();
    }

    private static string DescribeEnchantments(Enchantment[] enchantments) => string.Join("/", enchantments
        .Select(enchantment => (Name: Strip(EnchantmentsRegistry.All[enchantment.Id].Identifier), enchantment.Level))
        .OrderBy(enchantment => enchantment.Name, StringComparer.Ordinal)
        .Select(enchantment => $"{enchantment.Name}:{enchantment.Level}"));

    private static string Strip(string id) => id["minecraft:".Length..];
}
