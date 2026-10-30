using System.Collections.Immutable;
using Obsidian.API;
using Obsidian.API.Data;
using Obsidian.API.Effects;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using Obsidian.Nbt;
using Obsidian.Entities;
using Obsidian.Entities.Factories;
using Obsidian.Tests.Fakes;
using Obsidian.Registries;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData;
using System.Text.Json;
using System.Threading.Tasks;

namespace Obsidian.Tests;

/// <summary>
/// Parity checks for container loot. Expected slots and next draws come from vanilla 26.3's table evaluation
/// and container shuffling.
/// The capture has no world destination for explorer maps; named sequences use world seed 12345.
/// </summary>
[Collection(nameof(PluginTagFiles))]
public class VanillaLoot
{
    private const long WorldSeed = 12345L;

    [Fact]
    public void ContainerTablesMatchVanillaSlotsComponentsAndDraws()
    {
        // Load the 26.3 capture, including the new abandoned-camp chests and unresolved explorer maps.
        using var stream = typeof(VanillaLoot).Assembly.GetManifestResourceStream(
            "Obsidian.Tests.Assets.container_loot.json")!;
        using var capture = JsonDocument.Parse(stream);

        foreach (var row in capture.RootElement.EnumerateArray())
        {
            ReplayContainer(row);
        }
    }

    private static void ReplayContainer(JsonElement row)
    {
        // Read vanilla's saved item stacks so generated component patches participate in the comparison.
        var table = row.GetProperty("table").GetString()!;
        var seed = row.GetProperty("seed").GetInt64();
        var expectedSlots = new List<string>();
        foreach (var slot in row.GetProperty("slots").EnumerateObject())
        {
            var saved = CapturedCompound("", slot.Value);
            var stack = saved.ItemFromNbt();
            Assert.NotNull(stack);
            expectedSlots.Add($"{slot.Name}:{Describe(stack)}");
        }

        var actual = Fill(table, seed);

        var label = table + "/" + seed + ": ";
        Assert.Equal(label + string.Join(" ", expectedSlots), label + actual.Slots);
        Assert.Equal(row.GetProperty("nextInt").GetInt32(), actual.NextInt);
    }

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
            var type = Enum.Parse<EntityType>(row.GetProperty("type").GetString()!.ToPascalCase(), true);
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

    [Fact]
    public void ContextNumbersMatchVanillaValuesAndDraws()
    {
        // Load the actual vanilla results, including float bits and the following random draw.
        using var stream = typeof(VanillaLoot).Assembly.GetManifestResourceStream(
            "Obsidian.Tests.Assets.context_numbers.json")!;
        using var capture = JsonDocument.Parse(stream);

        foreach (var row in capture.RootElement.EnumerateArray())
        {
            if (!row.TryGetProperty("case", out _))
            {
                ReplayContextNumber(row);
            }
        }
    }

    public static IEnumerable<object[]> ContextNumberEdgeCases()
    {
        using var stream = typeof(VanillaLoot).Assembly.GetManifestResourceStream(
            "Obsidian.Tests.Assets.context_numbers.json")!;
        using var capture = JsonDocument.Parse(stream);

        return capture.RootElement.EnumerateArray()
            .Where(row => row.TryGetProperty("case", out _))
            .Select(row => row.GetProperty("case").GetString()!)
            .Distinct()
            .Select(name => new object[] { name })
            .ToArray();
    }

    [Theory]
    [MemberData(nameof(ContextNumberEdgeCases))]
    public void ContextNumberEdgeCasesMatchVanilla(string caseName)
    {
        using var stream = typeof(VanillaLoot).Assembly.GetManifestResourceStream(
            "Obsidian.Tests.Assets.context_numbers.json")!;
        using var capture = JsonDocument.Parse(stream);
        var rows = capture.RootElement.EnumerateArray()
            .Where(row => row.TryGetProperty("case", out var name) && name.GetString() == caseName)
            .ToArray();

        foreach (var row in rows)
        {
            ReplayContextNumber(row);
        }

        Assert.Equal(2, rows.Length);
    }

    [Fact]
    public void ComponentNumberReferencesMatchVanillaValuesAndDraws()
    {
        using var stream = typeof(VanillaLoot).Assembly.GetManifestResourceStream(
            "Obsidian.Tests.Assets.context_numbers.json")!;
        using var capture = JsonDocument.Parse(stream);
        var rows = capture.RootElement.EnumerateArray()
            .Where(row => row.GetProperty("provider").ValueKind == JsonValueKind.String)
            .ToArray();

        foreach (var row in rows)
        {
            ReplayComponentReference(row);
        }

        Assert.NotEmpty(rows);
    }

    private static void ReplayComponentReference(JsonElement row)
    {
        var random = new LegacyRandomSource(row.GetProperty("seed").GetInt64());
        var loot = NumberLootContext(row, random);
        Assert.True(BlocksRegistry.TryGetDefault("minecraft:furnace", out var defaultBlock));
        var block = loot.BlockState ?? defaultBlock;
        var provider = row.GetProperty("provider").GetString()!;
        var caller = new CapturedRandom(random);

        if (row.GetProperty("kind").GetString() == "int")
        {
            var value = new ComponentIntValue(0, provider);
            var actual = ComponentNumbers.Resolve(value, block, caller);

            Assert.Equal(row.GetProperty("value").GetInt32(), actual);
        }
        else
        {
            var value = new ComponentFloatValue(0, provider);
            var actual = ComponentNumbers.Resolve(value, block, caller);

            Assert.Equal(row.GetProperty("bits").GetInt32(), BitConverter.SingleToInt32Bits(actual));
        }

        Assert.Equal(row.GetProperty("nextInt").GetInt32(), random.NextInt());
    }

    // Supply vanilla's captured stream through the same System.Random entry point as fuel/compost callers.
    private sealed class CapturedRandom(LegacyRandomSource source) : Random
    {
        public override int Next() => source.NextInt();

        public override int Next(int maxValue) => source.NextInt(maxValue);

        public override float NextSingle() => source.NextFloat();

        public override double NextDouble() => source.NextDouble();
    }

    private static void ReplayContextNumber(JsonElement row)
    {
        // Each case starts a fresh stream and shares the loot predicate evaluator.
        var random = new LegacyRandomSource(row.GetProperty("seed").GetInt64());
        var loot = NumberLootContext(row, random);
        var expression = row.GetProperty("provider");
        var isInt = row.GetProperty("kind").GetString() == "int";
        var label = expression.GetRawText() + "/" + row.GetProperty("seed");

        // Evaluate through the public shared component used by the loot adapters.
        if (isInt)
        {
            var provider = ContextNumberProviders.ReadInt(expression);
            var actual = provider.GetInt(loot.Numbers);

            Assert.True(row.GetProperty("value").GetInt32() == actual, label + ": " + actual);
        }
        else
        {
            var provider = ContextNumberProviders.ReadFloat(expression);
            var actual = provider.GetFloat(loot.Numbers);
            var bits = BitConverter.SingleToInt32Bits(actual);

            Assert.True(row.GetProperty("bits").GetInt32() == bits, label + ": " + actual);
        }

        Assert.Equal(row.GetProperty("nextInt").GetInt32(), random.NextInt());
    }

    private static LootContext NumberLootContext(JsonElement row, LegacyRandomSource random)
    {
        IBlock? block = null;
        if (row.TryGetProperty("block", out var state))
        {
            Assert.True(BlocksRegistry.TryGetDefault(state.GetProperty("id").GetString()!, out block));
            if (state.TryGetProperty("properties", out var properties))
            {
                foreach (var property in properties.EnumerateObject())
                {
                    block = block.WithProperty(property.Name, property.Value.GetString()!);
                }
            }
        }

        return new LootContext
        {
            Random = random,
            BlockState = block,
            BlockProperties = block?.GetProperties() ?? ImmutableDictionary<string, string>.Empty
        };
    }

    private static (string Slots, int NextInt) Fill(string tableId, long seed)
    {
        var table = LootTables.All[tableId];
        var container = new Container();
        var random = table.CreateRandom(seed, new RandomSequences(WorldSeed));
        table.Fill(container, new LootContext { Random = random });

        var slots = new List<string>();
        for (var slot = 0; slot < container.Size; slot++)
        {
            var stack = container.GetItem(slot);
            if (stack is not null)
                slots.Add($"{slot}:{Describe(stack)}");
        }

        return (string.Join(" ", slots), random.NextInt());
    }

    private static INbtTag CapturedTag(string name, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => CapturedCompound(name, value),
        JsonValueKind.Array => CapturedList(name, value),
        JsonValueKind.String => new NbtTag<string>(name, value.GetString()!),
        JsonValueKind.True => new NbtTag<byte>(name, 1),
        JsonValueKind.False => new NbtTag<byte>(name, 0),
        _ when value.TryGetInt32(out var number) => new NbtTag<int>(name, number),
        _ => new NbtTag<float>(name, value.GetSingle())
    };

    // A vanilla codec JSON value as NBT, for the item readers.
    internal static NbtCompound CapturedCompound(string name, JsonElement value)
    {
        var compound = new NbtCompound(name);
        foreach (var property in value.EnumerateObject())
        {
            compound.Add(CapturedTag(property.Name, property.Value));
        }

        return compound;
    }

    private static NbtList CapturedList(string name, JsonElement value)
    {
        var entries = value.EnumerateArray().Select(element => CapturedTag("", element)).ToArray();
        var type = entries.Length == 0 ? NbtTagType.End : entries[0].Type;
        var list = new NbtList(type, name);
        foreach (var entry in entries)
        {
            list.Add(entry);
        }

        return list;
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
