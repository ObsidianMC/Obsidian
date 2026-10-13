using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using BlockPredicate = Obsidian.API.Loot.Predicates.BlockPredicate;
using Obsidian.API.Loot;
using Obsidian.API.Loot.Conditions;
using Obsidian.API.Loot.Predicates;
using Obsidian.API.Registries;
using Obsidian.Net;
using Obsidian.Registries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Parity checks for block drops. <c>block-loot-fixtures-1.21.11.json</c> (generated) holds inputs and the item stacks (in
/// vanilla order, empty stacks dropped) that vanilla 1.21.11's loot engine produced for them, captured headless by
/// <c>Obsidian.AssetGenerator/Java/BlockLootFixtures.java</c>.
/// </summary>
public class BlockLootParity
{
    private static readonly VectorD Origin = new(0.5, 64.5, 0.5);

    public static IEnumerable<object[]> Cases() => LoadCases().Select(fixture => new object[] { fixture.Name });

    [Theory]
    [MemberData(nameof(Cases))]
    public void DropsMatchVanilla(string name)
    {
        var fixture = LoadCases().Single(c => c.Name == name);

        Assert.Equal(fixture.Stacks, Generate(fixture));
    }

    [Fact]
    public void FixtureCoverageIsStable()
    {
        // Guards against silently losing fixtures: every vanilla case we captured must still be present.
        Assert.Equal(42, LoadCases().Count);
    }

    [Fact]
    public void MissingBlockPropertyFailsTheConditionInsteadOfMatching()
    {
        var wheatAge3 = BlockStateProperties.GetState("minecraft:wheat", new Dictionary<string, string> { ["age"] = "3" });
        var condition = new BlockStatePropertyCondition
        {
            Block = "minecraft:wheat",
            Properties = new Dictionary<string, string> { ["age"] = "7" }
        };

        var context = new LootContext
        {
            Random = new Obsidian.API.World.Generator.RandomSources.LegacyRandomSource(1L),
            Block = wheatAge3,
            BlockProperty = (block, name) => block.GetProperty(name),
        };
        var withoutReader = new LootContext
        {
            Random = new Obsidian.API.World.Generator.RandomSources.LegacyRandomSource(1L),
            Block = wheatAge3,
        };

        Assert.False(condition.Test(context));
        Assert.Throws<InvalidOperationException>(() => condition.Test(withoutReader));
    }

    [Fact]
    public void BlockPropertyNamesAreVanillaNames()
    {
        // persistent is a plain boolean in vanilla; the C# property name is different.
        var leaves = BlockStateProperties.GetState("minecraft:oak_leaves", new Dictionary<string, string> { ["persistent"] = "true", ["distance"] = "1" });

        Assert.Equal("true", leaves.GetProperty("persistent"));
        Assert.Equal("1", leaves.GetProperty("distance"));
    }

    [Theory]
    // Tall grass and large ferns check the other half: an upper half expects a lower one at Y-1, and a lower half an upper
    // one at Y+1. Vanilla can't evaluate these headless (they read the level), so this checks the condition's logic
    // against a block reader rather than captured vanilla output.
    [InlineData("upper", -1, "lower", "lower", true)]
    [InlineData("upper", -1, "lower", "upper", false)]
    [InlineData("lower", 1, "upper", "upper", true)]
    [InlineData("lower", 1, "upper", "lower", false)]
    public void LocationCheckReadsTheOtherHalf(string originHalf, int offsetY, string expectedHalf, string neighbourHalf, bool expected)
    {
        const string block = "minecraft:tall_grass";
        var origin = BlockStateProperties.GetState(block, new Dictionary<string, string> { ["half"] = originHalf });
        var neighbour = BlockStateProperties.GetState(block, new Dictionary<string, string> { ["half"] = neighbourHalf });
        var condition = new LocationCheckCondition
        {
            OffsetY = offsetY,
            Predicate = new LocationPredicate
            {
                Block = new BlockPredicate
                {
                    Blocks = block,
                    State = new Dictionary<string, string> { ["half"] = expectedHalf }
                }
            }
        };

        var context = new LootContext
        {
            Random = new Obsidian.API.World.Generator.RandomSources.LegacyRandomSource(1L),
            Origin = new VectorD(0.5, 64.5, 0.5),
            Block = origin,
            BlockProperty = (candidate, name) => candidate.GetProperty(name),
            BlockAt = (x, y, z) => y == 64 + offsetY ? neighbour : origin,
        };

        Assert.Equal(expected, condition.Test(context));
    }

    private static List<string> Generate(BlockLootFixture fixture)
    {
        if (fixture.TableAbsent)
            return [];

        var table = LootTables.All[fixture.Table];
        var block = BlocksRegistry.Get(fixture.State);
        var tool = fixture.Tool is null ? ItemStack.Air : BuildStack(fixture.Tool.Value);
        var context = new LootContext
        {
            Random = table.CreateRandom(fixture.Seed),
            Origin = Origin,
            Block = block,
            Tool = tool,
            ExplosionRadius = fixture.ExplosionRadius,
            BlockProperty = (candidate, property) => candidate.GetProperty(property),
        };

        return table.GetRandomItems(context)
            .Where(stack => stack.Count > 0)
            .Select(Describe)
            .ToList();
    }

    // The fixture's form: "item*count", then the block state copied onto the stack, as "{name=value,...}" in name order.
    private static string Describe(ItemStack stack)
    {
        var line = $"{stack.Holder.UnlocalizedName}*{stack.Count}";
        if (stack.GetComponent<SimpleDataComponent<BlockStateProperty[]>>(DataComponentType.BlockState)?.Value is { Length: > 0 } properties)
            line += " {" + string.Join(",", properties.OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => $"{property.Name}={property.Value}")) + "}";
        return line;
    }

    private static ItemStack BuildStack(JsonElement json)
    {
        var item = ItemsRegistry.Get(json.GetProperty("id").GetString()!);
        var stack = new ItemStack(item, json.TryGetProperty("count", out var count) ? count.GetInt32() : 1);
        if (json.TryGetProperty("components", out var components) && components.TryGetProperty("minecraft:enchantments", out var enchantments))
        {
            foreach (var enchantment in enchantments.EnumerateObject())
            {
                var definition = EnchantmentsRegistry.All.Single(candidate => candidate.Identifier == enchantment.Name);
                EnchantmentHelper.Enchant(stack, definition, enchantment.Value.GetInt32());
            }
        }

        return stack;
    }

    private static List<BlockLootFixture> LoadCases()
    {
        // Generated by Obsidian.AssetGenerator from the server jar, like the other vanilla assets.
        using var stream = typeof(NetworkBuffer).Assembly.GetManifestResourceStream("Obsidian.Assets.block-loot-fixtures-1.21.11.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().Select(element => new BlockLootFixture
        {
            Name = element.GetProperty("name").GetString()!,
            Table = element.GetProperty("table").GetString()!,
            State = element.GetProperty("state").GetInt32(),
            Seed = element.GetProperty("seed").GetInt64(),
            Tool = element.TryGetProperty("tool", out var tool) ? tool.Clone() : null,
            ExplosionRadius = element.TryGetProperty("explosionRadius", out var radius) ? (float)radius.GetDouble() : null,
            TableAbsent = element.TryGetProperty("tableAbsent", out var absent) && absent.GetBoolean(),
            Stacks = element.GetProperty("stacks").EnumerateArray().Select(stack => stack.GetString()!).ToList(),
        }).ToList();
    }

    private sealed class BlockLootFixture
    {
        public required string Name { get; init; }

        public required string Table { get; init; }

        public required int State { get; init; }

        public required long Seed { get; init; }

        public JsonElement? Tool { get; init; }

        public float? ExplosionRadius { get; init; }

        public bool TableAbsent { get; init; }

        public required IReadOnlyList<string> Stacks { get; init; }
    }
}
