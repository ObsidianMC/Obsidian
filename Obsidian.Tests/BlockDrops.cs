using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.ChunkData;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.Registries;
using Obsidian.API.World;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.ChunkData;
using Obsidian.Commands.Framework;
using Obsidian.Entities;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

[Collection(nameof(PluginTagFiles))]
public sealed class BlockDrops
{
    [Fact]
    public void VanillaBlockLootAndRandomDrawsMatchCapture()
    {
        // The Java probe executes actual 1.21.11 tables, plus vanilla's tool check. nextInt proves that skipped
        // alternatives and conditions, fortune and per-item explosion decay consume precisely the same draws.
        using var stream = typeof(BlockDrops).Assembly.GetManifestResourceStream(
            "Obsidian.Tests.Assets.block_loot.json")!;
        using var fixture = JsonDocument.Parse(stream);
        foreach (var row in fixture.RootElement.EnumerateArray())
        {
            var name = "minecraft:" + row.GetProperty("block").GetString();
            Assert.True(BlocksRegistry.TryGetDefault(name, out var block));
            foreach (var property in row.GetProperty("properties").EnumerateObject())
                block = block.WithProperty(property.Name, property.Value.GetString()!);

            var toolName = row.GetProperty("tool").GetString()!;
            var tool = toolName == "air" ? null : new ItemStack(ItemsRegistry.Get("minecraft:" + toolName));
            if (row.TryGetProperty("enchantment", out var enchantment))
            {
                var definition = EnchantmentsRegistry.All.Single(value =>
                    value.Identifier == "minecraft:" + enchantment.GetString());
                EnchantmentHelper.Enchant(tool!, definition, row.GetProperty("level").GetInt32());
            }

            var random = new LegacyRandomSource(row.GetProperty("seed").GetInt64());
            var radius = row.TryGetProperty("radius", out var radiusValue) ? radiusValue.GetSingle() : (float?)null;
            var drops = BlockLoot.GetDrops(block, tool, random, explosionRadius: radius,
                checkTool: row.GetProperty("checkTool").GetBoolean());
            var expected = row.GetProperty("drops").EnumerateArray().Select(value => value.GetString());
            var label = name + "/" + toolName + "/" + row.GetProperty("seed") + "/" + radius;
            Assert.Equal(label + ":" + string.Join(" ", expected), label + ":" + Describe(drops));
            Assert.Equal(row.GetProperty("nextInt").GetInt32(), random.NextInt());
        }
    }

    [Fact]
    public void ToolComponentRulesOverrideDefaultsAndEnforcePluginRequirements()
    {
        var tool = new ItemStack(ItemsRegistry.WoodenPickaxe);
        Assert.True(BlockLoot.HasCorrectTool(BlocksRegistry.Stone, tool));
        Assert.False(BlockLoot.HasCorrectTool(BlocksRegistry.DiamondOre, tool));
        var properties = new BlockProperties { RequiresCorrectTool = true };
        var block = BlocksRegistry.Register("blockloottests:ore", properties);
        block.Settings = BlockSettings.From(properties);
        Assert.False(BlockLoot.HasCorrectTool(block.DefaultState, tool));

        tool[DataComponentType.Tool] = new ToolDataComponent
        {
            DefaultMiningSpeed = 1,
            DamagePerBlock = 1,
            Rules =
            [
                new ToolRule
                {
                    Blocks = new IdSet
                    {
                        Type = 2,
                        Ids = [BlockIds.DiamondOre]
                    },
                    CorrectDropForBlocks = false
                },
                new ToolRule
                {
                    Blocks = new IdSet
                    {
                        Type = 3,
                        Ids = [BlockIds.DiamondOre, block.Id]
                    },
                    CorrectDropForBlocks = true
                }
            ]
        };
        Assert.False(BlockLoot.HasCorrectTool(BlocksRegistry.DiamondOre, tool));
        Assert.True(BlockLoot.HasCorrectTool(block.DefaultState, tool));
        tool.Remove(DataComponentType.Tool);
        Assert.False(BlockLoot.HasCorrectTool(BlocksRegistry.Stone, tool));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PluginTablesLoadOverrideReloadAndUnload()
    {
        await using var context = TestServerFactory.CreateServer($"loot-plugin-{Guid.NewGuid():N}");
        await using var manager = new PluginManager(context.Server, context.Provider, context.EventDispatcher,
            context.Provider.GetRequiredService<CommandHandler>(), NullLogger<PluginManager>.Instance,
            new ConfigurationBuilder().Build());
        var folder = Path.Join(AppContext.BaseDirectory, $"loot-plugin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var path = Path.Join(folder, "probe.obby");
        try
        {
            var files = new Dictionary<string, byte[]>
            {
                ["data/probe/loot_table/blocks/ore.json"] = Encoding.UTF8.GetBytes(ItemTable("probe:gem", 2)),
                ["data/minecraft/loot_table/blocks/stone.json"] = Encoding.UTF8.GetBytes(ItemTable("probe:gem", 3)),
                ["data/probe/loot_table/nested/reward.json"] =
                    Encoding.UTF8.GetBytes(ItemTable("minecraft:diamond", 4)),
                ["data/minecraft/tags/block/mineable/pickaxe.json"] =
                    Encoding.UTF8.GetBytes("""{ "values": ["probe:ore"] }""")
            };
            await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0", files));
            var plugin = await manager.LoadPluginAsync(path);
            Assert.NotNull(plugin);
            Assert.True(BlocksRegistry.TryGetDefault("probe:ore", out var ore));
            var type = Assert.IsType<BlockType>(ItemsRegistry.Get("probe:ore").Block);
            var original = type.Settings;
            type.Settings = original with { RequiresCorrectTool = true };
            Assert.True(BlockLoot.HasCorrectTool(ore, new ItemStack(ItemsRegistry.WoodenPickaxe)));
            Assert.False(BlockLoot.HasCorrectTool(ore, null));
            type.Settings = original;
            Assert.Equal("probe:gem*2", Describe(BlockLoot.GetDrops(ore, null, new LegacyRandomSource(1))));
            Assert.Equal("probe:gem*3", Describe(BlockLoot.GetDrops(BlocksRegistry.Stone,
                new ItemStack(ItemsRegistry.WoodenPickaxe), new LegacyRandomSource(1))));

            // An explicit table may be outside blocks/, and nested table references use the current overrides.
            var nested = LootTableJson.Parse("""
                { "pools": [{ "rolls": 1, "entries": [
                  { "type": "minecraft:loot_table", "value": "probe:nested/reward" }
                ] }] }
                """);
            Assert.Equal("minecraft:diamond*4", Describe(nested.GetRandomItems(new LootContext
            {
                Random = new LegacyRandomSource(1),
                ResolveTable = PluginLootTables.Resolve
            })));

            type.Settings = original with { LootTable = "probe:nested/reward" };
            Assert.Equal("minecraft:diamond*4", Describe(BlockLoot.GetDrops(ore, null, new LegacyRandomSource(1))));
            type.Settings = original;

            files["data/probe/loot_table/blocks/ore.json"] = Encoding.UTF8.GetBytes(ItemTable("probe:gem", 5));
            await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.1.0", files));
            plugin = await manager.LoadPluginAsync(path);
            Assert.NotNull(plugin);
            Assert.Equal("probe:gem*5", Describe(BlockLoot.GetDrops(ore, null, new LegacyRandomSource(1))));

            await manager.UnloadPluginAsync(plugin);
            Assert.Null(PluginLootTables.Resolve("probe:nested/reward"));
            Assert.Equal("minecraft:cobblestone*1", Describe(BlockLoot.GetDrops(BlocksRegistry.Stone,
                new ItemStack(ItemsRegistry.WoodenPickaxe), new LegacyRandomSource(1))));
            Assert.Equal("probe:ore*1", Describe(BlockLoot.GetDrops(ore, null, new LegacyRandomSource(1))));

            type.Settings = type.Settings with { LootTable = "probe:nested/reward" };
            Assert.Empty(BlockLoot.GetDrops(ore, null, new LegacyRandomSource(1)));
            type.Settings = type.Settings with { LootTable = null };
        }
        finally
        {
            // Only this test's directory, created under the test binary directory.
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void CopyStateAndComponentsPreserveRequestedValues()
    {
        var table = LootTableJson.Parse("""
            { "pools": [{ "rolls": 1, "entries": [{ "type": "minecraft:item", "name": "minecraft:oak_log",
              "functions": [
                { "function": "minecraft:copy_state", "block": "minecraft:oak_log", "properties": ["axis"] },
                { "function": "minecraft:copy_components", "source": "block_entity",
                  "include": ["minecraft:custom_name"] }
              ]
            }] }] }
            """);
        var block = BlocksRegistry.OakLog.WithProperty("axis", "x");
        var stack = Assert.Single(table.GetRandomItems(new LootContext
        {
            Random = new LegacyRandomSource(1),
            BlockState = block,
            BlockProperties = block.GetProperties(),
            BlockEntityComponents =
            [
                ComponentBuilder.CustomName with { Value = "Named log" },
                ComponentBuilder.Damage with { Value = 3 }
            ]
        }));
        Assert.Equal("Named log", stack.CustomName!.Text);
        Assert.Equal(0, stack.Damage);
        var properties = stack.GetComponent<SimpleDataComponent<ImmutableArray<BlockStateProperty>>>(
            DataComponentType.BlockState)!.Value;
        Assert.Equal("x", Assert.Single(properties).Value);
        Assert.Throws<InvalidDataException>(() => LootTableJson.Parse("""
            { "pools": [{ "rolls": 1, "conditions": [{ "condition": "plugin:unknown" }], "entries": [] }] }
            """));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PlayerBreakAndExplosionUseBlockTables()
    {
        await using var context = TestServerFactory.CreateServer($"block-break-loot-{Guid.NewGuid():N}");
        var chunk = AddChunk(context.World);
        var position = new Vector(8, 65, 8);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Miner",
            new VectorD(8, 65, 10));
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);
        player.GameMode = GameMode.Survival;
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.WoodenPickaxe));
        chunk.SetBlock(position, BlocksRegistry.Stone);
        await context.EventDispatcher.ExecuteEventAsync(new BlockBreakEventArgs(context.Server, player,
            BlocksRegistry.Stone, position, context.World));
        var drops = context.World.GetEntitiesInRange((VectorD)position, 4).OfType<ItemEntity>().ToArray();
        Assert.Equal("minecraft:cobblestone*1", Describe(drops.Select(drop => drop.Item)));
        Assert.True(chunk.GetBlock(position).IsAir);

        player.GameMode = GameMode.Creative;
        chunk.SetBlock(position, BlocksRegistry.Stone);
        await context.EventDispatcher.ExecuteEventAsync(new BlockBreakEventArgs(context.Server, player,
            BlocksRegistry.Stone, position, context.World));
        Assert.Single(context.World.GetEntitiesInRange((VectorD)position, 4).OfType<ItemEntity>());

        // Radius 1 keeps every item. Melon drops several slices rather than its own block item.
        chunk.SetBlock(position, BlocksRegistry.Melon);
        player.Position = (VectorD)position + 0.5;
        await ((AbstractLevel)context.World).ExplodeAsync(player, 1,
            interaction: AbstractLevel.ExplosionInteraction.Block);
        var slices = context.World.GetEntitiesInRange((VectorD)position, 4).OfType<ItemEntity>()
            .Where(drop => drop.Item.Holder == ItemsRegistry.MelonSlice).ToArray();
        Assert.Single(slices);
        Assert.InRange(slices[0].Item.Count, 3, 7);
        Assert.True(chunk.GetBlock(position).IsAir);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task LocationPredicatesReadBeforeRemoval()
    {
        await using var context = TestServerFactory.CreateServer($"block-location-loot-{Guid.NewGuid():N}");
        var chunk = AddChunk(context.World);
        var position = new Vector(8, 65, 8);
        var lower = BlocksRegistry.TallGrass.WithProperty("half", "lower");
        chunk.SetBlock(position, lower);
        chunk.SetBlock(position + Vector.Up, BlocksRegistry.TallGrass.WithProperty("half", "upper"));
        var tool = new ItemStack(ItemsRegistry.Shears);
        Assert.Equal("minecraft:short_grass*2", Describe(await BlockLoot.GetDropsAsync(context.World,
            position, lower, tool, new LegacyRandomSource(1))));
        chunk.SetBlock(position + Vector.Up, BlocksRegistry.Air);
        Assert.Empty(await BlockLoot.GetDropsAsync(context.World, position, lower, tool, new LegacyRandomSource(1)));
    }

    private static string ItemTable(string item, int count) => $$"""
        { "type": "minecraft:block", "pools": [{ "rolls": 1, "entries": [
          { "type": "minecraft:item", "name": "{{item}}", "functions": [
            { "function": "minecraft:set_count", "count": {{count}} }
          ] }
        ] }] }
        """;

    private static string Describe(IEnumerable<ItemStack> items) =>
        string.Join(" ", items.Select(item => item.Holder.UnlocalizedName + "*" + item.Count));

    private static IChunk AddChunk(World world)
    {
        var chunk = new Chunk(0, 0);
        chunk.SetChunkStatus(ChunkGenStage.full);
        world.LoadRegionByChunk(0, 0).SetChunk(chunk);
        return chunk;
    }
}
