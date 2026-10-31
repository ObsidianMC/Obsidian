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
using Obsidian.API.Loot.Conditions;
using Obsidian.API.Loot.Numbers;
using Obsidian.API.Registries;
using Obsidian.API.World;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.ChunkData;
using Obsidian.Commands.Framework;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

[Collection(nameof(PluginTagFiles))]
public sealed class BlockDrops
{
    [Fact]
    public void VanillaBlockLootAndRandomDrawsMatchCapture()
    {
        // The Java probe executes actual 26.3 tables, plus vanilla's tool check. nextInt proves that skipped
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
              "modifier": [
                { "type": "minecraft:copy_state", "block": "minecraft:oak_log", "properties": ["axis"] },
                { "type": "minecraft:copy_components", "source": "block_entity",
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
            { "pools": [{ "rolls": 1,
              "condition": { "type": "minecraft:all_of", "terms": [{ "type": "plugin:unknown" }] },
              "entries": [] }] }
            """));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PlayerBreakAndExplosionUseBlockTables()
    {
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

            var toolSlot = player.CurrentHeldItemSlot;
            var originalTool = player.Inventory.GetItem(toolSlot)!;
            Assert.Equal(1, originalTool.Damage);
            var replacement = new ItemStack(ItemsRegistry.WoodenPickaxe, 1,
                ComponentBuilder.Damage with { Value = ItemsRegistry.WoodenPickaxe.MaxDamage - 1 });
            var selectedSlot = toolSlot + 1;
            player.Inventory.SetItem(selectedSlot, replacement);
            player.CurrentHeldItemSlot = (short)(selectedSlot - 36);
            await player.DamageInventoryItemAsync(toolSlot, 1, expected: originalTool);
            Assert.Equal(2, originalTool.Damage);
            Assert.Equal(1, replacement.Count);

            // Wear waiting on a click must revalidate the identity, rather than destroy a replacement in that slot.
            ValueTask pendingWear;
            using (await player.Inventory.LockAsync())
            {
                pendingWear = player.DamageInventoryItemAsync(toolSlot, 1, expected: originalTool);
                Assert.False(pendingWear.IsCompleted);
                player.Inventory.SetItem(toolSlot, replacement);
            }

            await pendingWear;
            Assert.Equal(1, replacement.Count);
            Assert.Equal(replacement.Holder.MaxDamage - 1, replacement.Damage);
            Assert.Equal(2, originalTool.Damage);

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
            Assert.Empty(
                await BlockLoot.GetDropsAsync(context.World, position, lower, tool, new LegacyRandomSource(1)));
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task TwoPartPlayerBreakRemovesBothAndDropsOnce()
    {
        (string Name, string Property, string SelectedPart, string PartnerPart, string Item, bool Creative)[] cases =
        [
            ("oak_door", "half", "lower", "upper", "oak_door", false),
            ("red_bed", "part", "foot", "head", "red_bed", false),
            ("small_dripleaf", "half", "upper", "lower", "small_dripleaf", false),
            ("oak_door", "half", "upper", "lower", "oak_door", true)
        ];

        await FixtureCases.AssertAllAsync(cases, row => $"{row.Name}, {row.Property}, {row.SelectedPart}, {row.PartnerPart}, {row.Item}, {row.Creative}",
            row => AssertTwoPartPlayerBreakRemovesBothAndDropsOnce(
                row.Name, row.Property, row.SelectedPart, row.PartnerPart, row.Item, row.Creative));
    }

    private static async Task AssertTwoPartPlayerBreakRemovesBothAndDropsOnce(
        string name, string property, string selectedPart, string partnerPart, string item, bool creative)
    {
        await using var context = TestServerFactory.CreateServer($"two-part-loot-{Guid.NewGuid():N}");
        var chunk = AddChunk(context.World);
        var position = new Vector(8, 65, 8);
        Assert.True(BlocksRegistry.TryGetDefault("minecraft:" + name, out var defaultBlock));
        var block = defaultBlock.WithProperty(property, selectedPart);
        if (name == "pitcher_crop")
            block = block.WithProperty("age", "4");

        if (property == "part")
            block = block.WithProperty("facing", "east");

        if (name == "small_dripleaf")
            block = block.WithProperty("waterlogged", "true");

        var partner = block.WithProperty(property, partnerPart);
        var offset = property == "part"
            ? selectedPart == "foot" ? Vector.East : Vector.West
            : selectedPart == "lower" ? Vector.Up : Vector.Down;
        chunk.SetBlock(position, block);
        chunk.SetBlock(position + offset, partner);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Miner",
            new VectorD(8, 65, 10));
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);
        player.GameMode = creative ? GameMode.Creative : GameMode.Survival;
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Shears));
        await context.EventDispatcher.ExecuteEventAsync(new BlockBreakEventArgs(context.Server, player,
            block, position, context.World));
        Assert.True(chunk.GetBlock(position).IsAir);
        Assert.Equal(name == "small_dripleaf" ? BlockIds.Water : BlockIds.Air,
            chunk.GetBlock(position + offset).RegistryId);
        var drops = context.World.GetEntitiesInRange((VectorD)position, 4).OfType<ItemEntity>().ToArray();
        if (creative)
        {
            Assert.Empty(drops);
        }
        else
        {
            var drop = Assert.Single(drops);
            Assert.Equal("minecraft:" + item, drop.Item.Holder.UnlocalizedName);
            Assert.Equal(name == "tall_grass" ? 2 : 1, drop.Item.Count);
        }

        foreach (var drop in drops)
            await drop.RemoveAsync();

        // Drain shape requests for partners the player's two-part transaction already removed.
        await context.World.DoWorldTickAsync();

        // Environmental destruction uses each half's own tool-less loot, even with a creative player nearby.
        // Beds survive loss of the blocks below; their missing-part update drops only the head's item.
        var lower = defaultBlock.WithProperty(property, property == "part" ? "foot" : "lower");
        if (property == "part")
            lower = lower.WithProperty("facing", "east");

        var partnerPosition = position + (property == "part" ? Vector.East : Vector.Up);
        chunk.SetBlock(position + Vector.Down, BlocksRegistry.Clay);
        chunk.SetBlock(position, lower);
        chunk.SetBlock(partnerPosition, lower.WithProperty(property, property == "part" ? "head" : "upper"));
        var client = (TestClient)player.Client;
        player.LoadedChunks.Add(0);
        client.ClearPackets();
        await context.World.SetBlockAsync(position + Vector.Down, BlocksRegistry.Air, BlockUpdateFlags.All);
        await context.World.DoWorldTickAsync();
        if (property == "part")
        {
            Assert.True(chunk.GetBlock(position).IsSameState(lower));
            Assert.Equal("head", chunk.GetBlock(partnerPosition).GetProperty("part"));
            await context.World.SetBlockAsync(position, BlocksRegistry.Air, BlockUpdateFlags.All);
        }

        await context.World.DoWorldTickAsync();
        Assert.True(chunk.GetBlock(position).IsAir);
        Assert.True(chunk.GetBlock(partnerPosition).IsAir);
        Assert.Equal(name == "small_dripleaf" ? "" : "minecraft:" + item + "*1",
            Describe(context.World.GetEntitiesInRange((VectorD)position, 4)
                .OfType<ItemEntity>().Select(drop => drop.Item)));
        Assert.Equal(property == "part" ? 1 : 2,
            client.SentPackets.OfType<LevelEventPacket>().Count(packet => packet.Type == 2001
                && (packet.Position == position || packet.Position == partnerPosition)));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ConcurrentNamedSequenceEvaluationsKeepWholeDrawGroups()
    {
        const string sequenceId = "blockloottests:shared";
        var level = DispatchProxy.Create<ILevel, LootLevelProxy>();
        var sequence = BlockLoot.SequencesFor(level).Get(sequenceId);
        var expectedRandom = new RandomSequences(123).Get(sequenceId);
        var expected = Enumerable.Range(0, 64).Select(_ =>
            $"minecraft:apple*{expectedRandom.NextInt(10) + 1} minecraft:carrot*{expectedRandom.NextInt(10) + 1}")
            .Order(StringComparer.Ordinal).ToArray();
        var table = LootTableJson.Parse("""
            { "random_sequence": "blockloottests:shared", "pools": [{ "rolls": 1, "entries": [
              { "type": "minecraft:dynamic", "name": "blockloottests:draw_pair" }
            ] }] }
            """, "minecraft:blocks/oak_log");
        var active = 0;
        var overlaps = 0;
        void DrawPair(string name, Action<ItemStack> output)
        {
            if (Interlocked.Increment(ref active) != 1)
                Interlocked.Increment(ref overlaps);

            output(new ItemStack(ItemsRegistry.Apple, sequence.NextInt(10) + 1));
            Thread.Sleep(1);
            output(new ItemStack(ItemsRegistry.Carrot, sequence.NextInt(10) + 1));
            Interlocked.Decrement(ref active);
        }

        PluginLootTables.Apply([table]);
        try
        {
            using var start = new ManualResetEventSlim();
            var workers = Enumerable.Range(0, 8).Select(_ => Task.Factory.StartNew(async () =>
            {
                start.Wait();
                var results = new List<string>();
                for (var i = 0; i < 8; i++)
                {
                    results.Add(Describe(await BlockLoot.GetDropsAsync(level, Vector.Zero, BlocksRegistry.OakLog,
                        null, dynamicDrops: DrawPair)));
                }

                return results;
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap()).ToArray();
            start.Set();
            var actual = (await Task.WhenAll(workers)).SelectMany(result => result).Order(StringComparer.Ordinal);
            Assert.Equal(0, overlaps);
            Assert.Equal(expected, actual);
            Assert.Equal(expectedRandom.NextInt(), sequence.NextInt());
        }
        finally
        {
            PluginLootTables.Apply([]);
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ReloadDuringLocationReadKeepsRootNestedTablesAndSequenceSnapshot()
    {
        var level = DispatchProxy.Create<ILevel, LootLevelProxy>();
        var proxy = (LootLevelProxy)level;
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource<IBlock?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = new List<Vector>();
        proxy.ReadBlock = position =>
        {
            reads.Add(position);
            readStarted.TrySetResult();
            return new ValueTask<IBlock?>(resume.Task);
        };
        var rootA = ReferenceTable("blockloottests:sequence_a");
        var rootB = ReferenceTable("blockloottests:sequence_b");
        PluginLootTables.Apply([rootA, LocationTable("minecraft:apple", 1)]);
        try
        {
            var pending = BlockLoot.GetDropsAsync(level, Vector.Zero, BlocksRegistry.OakLog, null).AsTask();
            await readStarted.Task;
            PluginLootTables.Apply([rootB, LocationTable("minecraft:carrot", 2)]);
            resume.SetResult(BlocksRegistry.Stone);
            var randomA = new RandomSequences(123).Get(rootA.RandomSequence!);
            Assert.Equal($"minecraft:apple*{randomA.NextInt(10) + 1}", Describe(await pending));
            Assert.Equal([new Vector(0, 1, 0)], reads);
            Assert.Equal(randomA.NextInt(), BlockLoot.SequencesFor(level).Get(rootA.RandomSequence!).NextInt());

            var randomB = new RandomSequences(123).Get(rootB.RandomSequence!);
            Assert.Equal($"minecraft:carrot*{randomB.NextInt(10) + 1}", Describe(await BlockLoot.GetDropsAsync(
                level, Vector.Zero, BlocksRegistry.OakLog, null)));
            Assert.Equal([new Vector(0, 1, 0), new Vector(0, 2, 0)], reads);
            Assert.Equal(randomB.NextInt(), BlockLoot.SequencesFor(level).Get(rootB.RandomSequence!).NextInt());
        }
        finally
        {
            resume.TrySetResult(BlocksRegistry.Stone);
            PluginLootTables.Apply([]);
        }
    }

    [Fact]
    public void MalformedTableReferenceIsRejectedAndLoaderSkipsIt()
    {
        (string CaseId, string Value)[] cases =
        [
            ("missing", ""),
            ("number", ", \"value\": 1")
        ];

        FixtureCases.AssertAll(cases, row => $"{row.CaseId}, {row.Value}",
            row => AssertMalformedTableReferenceIsRejectedAndLoaderSkipsIt(row.CaseId, row.Value));
    }

    private static void AssertMalformedTableReferenceIsRejectedAndLoaderSkipsIt(string caseId, string value)
    {
        var json = $$"""
            { "pools": [{ "rolls": 1, "entries": [{ "type": "minecraft:loot_table"{{value}} }] }] }
            """;
        Assert.Throws<InvalidDataException>(() => LootTableJson.Parse(json));
        var files = new Dictionary<string, byte[]>
        {
            ["data/blockloottests/loot_table/bad.json"] = Encoding.UTF8.GetBytes(json),
            ["data/blockloottests/loot_table/good.json"] = Encoding.UTF8.GetBytes(ItemTable("minecraft:apple", 1))
        };
        var loaded = PluginLootTables.Read(new DataFiles("test", caseId, files.Keys.ToArray(),
            path => files.GetValueOrDefault(path)), NullLogger.Instance);
        Assert.Equal("blockloottests:good", Assert.Single(loaded).Identifier);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task FailedLootAndUnmatchedPartsDoNotCorruptBlockRemoval()
    {
        {
            await using var context = TestServerFactory.CreateServer($"explosion-loot-failure-{Guid.NewGuid():N}");
            var chunk = AddChunk(context.World);
            var position = new Vector(8, 65, 8);
            chunk.SetBlock(position, BlocksRegistry.ShortGrass);
            chunk.SetBlock(position + Vector.East, BlocksRegistry.Dandelion);
            context.World.LevelData.GameRules["block_explosion_drop_decay"] = "false";
            PluginLootTables.Apply([new LootTable
            {
                Identifier = "minecraft:blocks/short_grass",
                Pools =
                [
                    new LootPool
                    {
                        Rolls = new ConstantNumber { Value = 1 },
                        Entries = [],
                        Conditions = [new ThrowingLootCondition()]
                    }
                ]
            }]);
            try
            {
                await context.World.ExplodeAsync((VectorD)position + 0.5, 2, false,
                    AbstractLevel.ExplosionInteraction.Block);
                Assert.True(chunk.GetBlock(position).IsAir);
                Assert.True(chunk.GetBlock(position + Vector.East).IsAir);
                Assert.Equal("minecraft:dandelion*1", Describe(context.World.GetEntitiesInRange((VectorD)position, 4)
                    .OfType<ItemEntity>().Select(drop => drop.Item)));
            }
            finally
            {
                PluginLootTables.Apply([]);
            }
        }

        {
            await using var context = TestServerFactory.CreateServer($"unmatched-loot-{Guid.NewGuid():N}");
            var chunk = AddChunk(context.World);
            var position = new Vector(8, 65, 8);
            var upper = BlocksRegistry.OakDoor.WithProperty("half", "upper");
            chunk.SetBlock(position, upper);
            chunk.SetBlock(position + Vector.Down, BlocksRegistry.Stone);
            var parts = await TwoPartBlockBreak.PrepareAsync(context.World, position, upper);
            Assert.Null(parts.PartnerPosition);
            await parts.RemovePartnerAsync(context.World);
            Assert.True(chunk.GetBlock(position + Vector.Down).Is(BlockIds.Stone));
            Assert.Empty(await BlockLoot.GetDropsAsync(context.World, parts.DropPosition, parts.DropState, null));

            // A supported double flower drops once from its lower half; the upper half has an empty loot result.
            await context.World.GetChunkAsync(0, 0, scheduleGeneration: false);
            chunk.SetBlock(position + Vector.Down, BlocksRegistry.Dirt);
            chunk.SetBlock(position, BlocksRegistry.Sunflower.WithProperty("half", "lower"));
            chunk.SetBlock(position + Vector.Up, BlocksRegistry.Sunflower.WithProperty("half", "upper"));
            await context.World.SetBlockAsync(position + Vector.Down, BlocksRegistry.Air, BlockUpdateFlags.All);
            await context.World.DoWorldTickAsync();
            await context.World.DoWorldTickAsync();
            Assert.True(chunk.GetBlock(position).IsAir);
            Assert.True(chunk.GetBlock(position + Vector.Up).IsAir);
            var flower = Assert.Single(context.World.GetEntitiesInRange((VectorD)position, 4).OfType<ItemEntity>());
            Assert.True(flower.Item.Is(ItemsRegistry.Sunflower));
            Assert.Equal(1, flower.Item.Count);
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ExplosionDestroysTwoPartBlocksAndDropsOnce()
    {
        (string Name, string Property, string SelectedPart)[] cases =
        [
            ("oak_door", "half", "upper"),
            ("red_bed", "part", "foot")
        ];

        await FixtureCases.AssertAllAsync(cases, row => $"{row.Name}, {row.Property}, {row.SelectedPart}",
            row => AssertExplosionDestroysTwoPartBlocksAndDropsOnce(row.Name, row.Property, row.SelectedPart));
    }

    private static async Task AssertExplosionDestroysTwoPartBlocksAndDropsOnce(string name, string property, string selectedPart)
    {
        await using var context = TestServerFactory.CreateServer($"two-part-explosion-{Guid.NewGuid():N}");
        var chunk = AddChunk(context.World);
        var position = new Vector(8, 65, 8);
        Assert.True(BlocksRegistry.TryGetDefault("minecraft:" + name, out var block));
        block = block.WithProperty(property, selectedPart);
        var partnerPart = property == "half"
            ? selectedPart == "upper" ? "lower" : "upper"
            : selectedPart == "foot" ? "head" : "foot";
        Vector offset;
        if (property == "half")
        {
            offset = selectedPart == "lower" ? Vector.Up : Vector.Down;
        }
        else
        {
            block = block.WithProperty("facing", "east");
            offset = selectedPart == "foot" ? Vector.East : Vector.West;
        }

        chunk.SetBlock(position, block);
        chunk.SetBlock(position + offset, block.WithProperty(property, partnerPart));
        context.World.LevelData.GameRules["block_explosion_drop_decay"] = "false";
        await context.World.ExplodeAsync((VectorD)position + 0.5, 3, false,
            AbstractLevel.ExplosionInteraction.Block);
        Assert.True(chunk.GetBlock(position).IsAir);
        Assert.True(chunk.GetBlock(position + offset).IsAir);
        Assert.Equal("minecraft:" + name + "*1", Describe(context.World.GetEntitiesInRange((VectorD)position, 4)
            .OfType<ItemEntity>().Select(drop => drop.Item)));
    }

    private static LootTable ReferenceTable(string sequence) => LootTableJson.Parse($$"""
        { "random_sequence": "{{sequence}}", "pools": [{ "rolls": 1, "entries": [
          { "type": "minecraft:loot_table", "value": "blockloottests:nested" }
        ] }] }
        """, "minecraft:blocks/oak_log");

    private static LootTable LocationTable(string item, int offset) => LootTableJson.Parse($$"""
        { "pools": [{ "rolls": 1, "condition": { "type": "minecraft:all_of", "terms": [
          { "type": "minecraft:location_check", "offsetY": {{offset}},
            "predicate": { "block": { "blocks": "minecraft:stone" } } }
        ] }, "entries": [{ "type": "minecraft:item", "name": "{{item}}", "modifier": [
          { "type": "minecraft:set_count", "count": { "type": "minecraft:uniform", "min": 1, "max": 10 } }
        ] }] }] }
        """, "blockloottests:nested");

    private sealed class ThrowingLootCondition : ILootCondition
    {
        public bool Test(LootContext context) => throw new InvalidOperationException("Deliberate loot failure.");
    }

    public class LootLevelProxy : DispatchProxy
    {
        internal Func<Vector, ValueTask<IBlock?>> ReadBlock { get; set; } =
            _ => ValueTask.FromResult<IBlock?>(BlocksRegistry.Air);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.Name switch
        {
            "get_Seed" => "123",
            "GetBlockAsync" => this.ReadBlock((Vector)args![0]!),
            _ => throw new NotSupportedException(targetMethod.Name)
        };
    }

    private static string ItemTable(string item, int count) => $$"""
        { "type": "minecraft:block", "pools": [{ "rolls": 1, "entries": [
          { "type": "minecraft:item", "name": "{{item}}", "modifier": [
            { "type": "minecraft:set_count", "count": {{count}} }
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
