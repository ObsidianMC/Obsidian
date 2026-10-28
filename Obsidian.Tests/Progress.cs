using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API.Advancements;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Commands.Framework;
using Obsidian.Entities;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Plugins;
using Obsidian.Progress;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

[Collection(nameof(PluginTagFiles))]
public sealed class Progress
{
    [Fact]
    public async Task StatisticsReplayVanillaSaveAndAwardPacket()
    {
        var reference = JsonNode.Parse(Read("progress-stats.json"))!.AsObject();
        var statistics = new PlayerStatistics();
        statistics.Load(reference);
        Assert.Equal(1200, statistics.Get("minecraft:play_time"));
        Assert.Equal(2, statistics.Get("minecraft:jump"));
        // The capture is a 1.21.11 save; a save stamps the current data version.
        var expected = reference.DeepClone().AsObject();
        expected["DataVersion"] = ServerConstants.DataVersion;
        Assert.True(JsonNode.DeepEquals(expected, statistics.Save()));
        var bytes = new NetworkBuffer();
        statistics.CreatePacket().Serialize(bytes);
        Assert.Equal(Read("progress-stats.bin"), bytes.AsSpan(0, bytes.Size).ToArray());

        reference["stats"]!["missing:type"] = new JsonObject { ["missing:stat"] = 37 };
        statistics.Load(reference);
        statistics.Increment("minecraft:play_time", int.MaxValue);
        Assert.Equal(int.MaxValue, statistics.Get("minecraft:play_time"));
        Assert.Equal(37, statistics.Save()["stats"]!["missing:type"]!["missing:stat"]!.GetValue<int>());
        Assert.Throws<ArgumentException>(() => statistics.Increment("missing:stat"));

        await using var context = TestServerFactory.CreateServer($"progress-stats-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Statistician");
        await player.LoadAsync(loadFromPersistentWorld: false);
        await player.TickAsync();
        Assert.Equal(1, player.Statistics.Get("minecraft:play_time"));
        var chunk = (await context.World.GetChunkAsync(0, 0, false))!;
        chunk.SetChunkStatus(Obsidian.API.ChunkGenStage.full);
        await player.UpdateAsync(player.Position + new Obsidian.API.VectorD(1, 0, 0),
            Obsidian.API.MovementFlags.OnGround);
        Assert.Equal(100, player.Statistics.Get("minecraft:walk_one_cm"));
        player.Statistics.Increment("minecraft:jump", 2);
        var request = new ClientCommandPacket();
        request.Populate(new NetworkBuffer([1]));
        await request.HandleAsync(context.Server, player);
        Assert.Contains(((TestClient)player.Client).SentPackets.OfType<AwardStatsPacket>(), packet =>
            packet.Stats.Any(stat => stat.CategoryId == CategoryIds.Custom
                && stat.StatisticId == 23 && stat.Value == 2));
        await player.SaveAsync();
        var root = Path.GetDirectoryName(context.World.PlayerDataPath)!;
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(Path.Join(root, "stats", $"{player.Uuid}.json")))!;
        Assert.Equal(2, saved["stats"]!["minecraft:custom"]!["minecraft:jump"]!.GetValue<int>());
        await player.KillAsync(player, Obsidian.API.ChatMessage.Simple("Environmental death"));
        await player.KillAsync(player, Obsidian.API.ChatMessage.Simple("Duplicate notification"));
        Assert.Equal(1, player.Statistics.Get("minecraft:deaths"));
        Assert.Equal(0, player.Statistics.Get("minecraft:time_since_death"));
        Assert.False(player.Advancements.IsDone("minecraft:adventure/root"));

        // Either the victim's or the killer's tick can be waiting for the death before a deferred respawn.
        var killer = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Killer");
        using var deathJson = JsonDocument.Parse("""
            {
              "criteria": {
                "death": { "trigger": "minecraft:entity_killed_player" },
                "kill": { "trigger": "minecraft:player_killed_entity" }
              }
            }
            """);
        var definitions = AdvancementsRegistry.Current.Values.ToArray();
        try
        {
            AdvancementsRegistry.Apply([AdvancementsRegistry.Parse("probe:death", deathJson.RootElement)]);
            Entities.Player[] tickingPlayers = [player, killer];
            foreach (var ticking in tickingPlayers)
            {
                await player.RespawnAsync();
                player.Advancements.Revoke("probe:death");
                killer.Advancements.Revoke("probe:death");
                player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.Diamond));
                var packets = ((TestClient)player.Client).SentPackets;
                var deaths = packets.OfType<PlayerCombatKillPacket>().Count();
                var pending = new DeferredInventoryWork();
                var tickGate = (SemaphoreSlim)typeof(Entities.PlayerBase)
                    .GetField("tickGate", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(ticking)!;
                await tickGate.WaitAsync();
                Task dying = Task.CompletedTask;
                Task respawning = Task.CompletedTask;
                try
                {
                    using (await player.Inventory.LockAsync())
                    {
                        dying = player.KillAsync(killer, "Death beside a tick").AsTask();
                        Assert.False(dying.IsCompleted);
                        using (pending.Enter())
                            await player.RespawnAsync();
                    }

                    // This drain stands in for the tick's inventory hook exit, still holding tickGate.
                    respawning = pending.RunAsync(NullLogger.Instance).AsTask();
                    await respawning.WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.True(player.Alive);
                    Assert.All(player.Inventory, Assert.Null);
                    Assert.Equal(deaths + 1, packets.OfType<PlayerCombatKillPacket>().Count());
                    Assert.False(ticking.Advancements.HasCriterion(
                        "probe:death", ReferenceEquals(ticking, player) ? "death" : "kill"));
                }
                finally
                {
                    tickGate.Release();
                    await Task.WhenAll(dying, respawning).WaitAsync(TimeSpan.FromSeconds(10));
                }

                Assert.True(player.Advancements.HasCriterion("probe:death", "death"));
                Assert.True(killer.Advancements.HasCriterion("probe:death", "kill"));
            }

            Assert.Equal(3, player.Statistics.Get("minecraft:deaths"));
            Assert.Equal(2, killer.Statistics.Get("minecraft:player_kills"));
        }
        finally
        {
            AdvancementsRegistry.Apply(definitions);
        }
    }

    [Fact]
    public async Task AdvancementReplayAndInventoryRewardUsePlayerOwnedProgress()
    {
        using var definitionJson = JsonDocument.Parse("""
            {
              "criteria": {
                "alternate": { "trigger": "minecraft:impossible" },
                "first": { "trigger": "minecraft:impossible" },
                "second": { "trigger": "minecraft:impossible" }
              },
              "requirements": [["first", "alternate"], ["second"]],
              "sends_telemetry_event": true
            }
            """);
        var definition = AdvancementsRegistry.Parse("probe:progress", definitionJson.RootElement);
        using var inventoryJson = JsonDocument.Parse("""
            {
              "criteria": {
                "obtain": {
                  "trigger": "minecraft:inventory_changed",
                  "conditions": { "items": [{ "items": "minecraft:crafting_table" }] }
                }
              },
              "rewards": { "experience": 7, "loot": ["probe:reward"], "recipes": ["minecraft:crafting_table"] }
            }
            """);
        var inventory = AdvancementsRegistry.Parse("probe:inventory", inventoryJson.RootElement);
        using var unsupportedJson = JsonDocument.Parse("""
            {
              "criteria": {
                "obtain": {
                  "trigger": "minecraft:inventory_changed",
                  "conditions": { "items": [{ "items": "minecraft:crafting_table", "count": { "unknown": 1 } }] }
                }
              }
            }
            """);
        var unsupported = AdvancementsRegistry.Parse("probe:unsupported", unsupportedJson.RootElement);
        await using var context = TestServerFactory.CreateServer($"progress-advancement-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Advancer");
        await player.LoadAsync(loadFromPersistentWorld: false);
        var client = (TestClient)player.Client;
        try
        {
            AdvancementsRegistry.Apply([definition, inventory, unsupported]);
            var saved = new JsonObject
            {
                ["probe:progress"] = JsonNode.Parse(Read("progress-advancement.json")),
                ["DataVersion"] = 4671
            };
            player.ProgressAdvancements.Load(saved);
            Assert.True(player.Advancements.HasCriterion("probe:progress", "first"));
            Assert.False(player.Advancements.IsDone("probe:progress"));
            await player.ProgressAdvancements.FlushAsync(initial: true);
            var packet = client.SentPackets.OfType<UpdateAdvancementsPacket>().Last();
            packet.Added = [definition];
            packet.Progress = new Dictionary<string, Dictionary<string, DateTimeOffset?>>
            {
                ["probe:progress"] = packet.Progress["probe:progress"]
            };
            var bytes = new NetworkBuffer();
            packet.Serialize(bytes);
            Assert.Equal(Read("progress-advancements.bin"), bytes.AsSpan(0, bytes.Size).ToArray());
            Assert.True(await player.Advancements.GrantAsync("probe:progress", "second"));
            Assert.True(player.Advancements.IsDone("probe:progress"));
            Assert.True(player.Advancements.Revoke("probe:progress", "first"));
            Assert.False(player.Advancements.IsDone("probe:progress"));

            PluginLootTables.Apply([Obsidian.API.Loot.LootTableJson.Parse("""
                { "pools": [{ "rolls": 1, "entries": [{ "type": "minecraft:item", "name": "minecraft:diamond" }] }] }
                """, "probe:reward")]);
            player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.CraftingTable));
            Task grant;
            Task save;
            Task leave;
            using (await player.Inventory.LockAsync())
            {
                // Completion has started, but reward inventory mutation is blocked. Saving and leaving must wait.
                grant = player.FireProgressAsync("inventory_changed").AsTask();
                Assert.False(grant.IsCompleted);
                save = player.SaveAsync();
                leave = player.StopTickingAsync();
                Assert.False(save.IsCompleted);
                Assert.False(leave.IsCompleted);
            }

            await Task.WhenAll(grant, save, leave);
            Assert.True(player.Advancements.IsDone("probe:inventory"));
            Assert.False(player.Advancements.HasCriterion("probe:unsupported", "obtain"));
            Assert.Equal(7, player.XpTotal);
            Assert.Contains(player.Inventory, item => item?.Holder == ItemsRegistry.Diamond);
            Assert.Contains(client.SentPackets.OfType<RecipeBookAddPacket>(), book =>
                book.Entries.Any(entry =>
                    AdvancementsRegistry.Qualify(entry.Recipe.Identifier) == "minecraft:crafting_table"));
            await player.FireProgressAsync("inventory_changed");
            Assert.Equal(7, player.XpTotal);
            await player.ProgressAdvancements.FlushAsync();
            Assert.False(client.SentPackets.OfType<UpdateAdvancementsPacket>().Last().Reset);
            var other = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Other");
            Assert.False(other.Advancements.IsDone("probe:inventory"));
            await player.SaveAsync();
            var root = Path.GetDirectoryName(context.World.PlayerDataPath)!;
            var path = Path.Join(root, "advancements", $"{player.Uuid}.json");
            var file = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            Assert.Equal(ServerConstants.DataVersion, file["DataVersion"]!.GetValue<int>());
            Assert.True(file["probe:inventory"]!["done"]!.GetValue<bool>());
            var reloaded = new PlayerAdvancements(player);
            reloaded.Load(file.AsObject());
            Assert.True(reloaded.IsDone("probe:inventory"));
            await player.LoadAsync(loadFromPersistentWorld: false);
            Assert.Equal(7, player.XpTotal);
            Assert.Contains(player.Inventory, item => item?.Holder == ItemsRegistry.Diamond);
            Assert.False(await player.Advancements.GrantAsync("probe:progress", "first"));
            Assert.False(player.Advancements.HasCriterion("probe:progress", "first"));
        }
        finally
        {
            AdvancementsRegistry.Apply([]);
            PluginLootTables.Apply([]);
        }
    }

    [Fact]
    public async Task PluginTriggerAndCustomStatisticReleaseAndRestoreOnReload()
    {
        await using var context = TestServerFactory.CreateServer($"progress-plugin-{Guid.NewGuid():N}");
        await using var manager = new PluginManager(context.Server, context.Provider, context.EventDispatcher,
            context.Provider.GetRequiredService<CommandHandler>(), NullLogger<PluginManager>.Instance,
            new ConfigurationBuilder().Build());
        var root = Path.GetDirectoryName(context.World.PlayerDataPath)!;
        var path = Path.Join(root, "progress.obby");
        var files = new Dictionary<string, byte[]>
        {
            ["data/probe/advancement/progress.json"] = Encoding.UTF8.GetBytes("""
                {
                  "criteria": {
                    "one": { "trigger": "probe:event", "conditions": { "value": 7 } },
                    "two": { "trigger": "probe:event", "conditions": { "value": 8 } }
                  }
                }
                """)
        };
        await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0", files));
        var container = (await manager.LoadPluginAsync(path))!;
        var plugin = container.Plugin!;
        var type = plugin.GetType();
        var trigger = (AdvancementTrigger<int>)type.GetProperty("ProgressTrigger")!.GetValue(plugin)!;
        var statId = (int)type.GetProperty("ProgressStatisticId")!.GetValue(plugin)!;
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Modded");
        await player.LoadAsync(loadFromPersistentWorld: false);
        player.Statistics.Increment("probe:steps", 4);
        Assert.Equal(4, player.Statistics.Get("probe:steps"));
        Assert.Contains(context.Server.GetModdedRegistries(), registry => registry.Name == "minecraft:custom_stat"
            && registry.Entries.Any(entry => entry.Identifier == "probe:steps" && entry.Id == statId));
        await trigger.FireAsync(player, value => value == 99);
        Assert.False(player.Advancements.HasCriterion("probe:progress", "one"));
        await using (DeferredInventoryWork.Begin(NullLogger.Instance))
        {
            using (await player.Inventory.LockAsync())
            {
                await trigger.FireAsync(player, value => value == 7);
                Assert.False(player.Advancements.HasCriterion("probe:progress", "one"));
            }
        }

        Assert.True(player.Advancements.HasCriterion("probe:progress", "one"));
        Assert.False(player.Advancements.IsDone("probe:progress"));
        await using (DeferredInventoryWork.Begin(NullLogger.Instance))
        {
            await trigger.FireAsync(player, value => value == 8);
            await manager.UnloadPluginAsync(container);
        }

        Assert.Throws<ArgumentException>(() => player.Statistics.Increment("probe:steps"));
        await trigger.FireAsync(player, _ => true);
        var saved = player.ProgressAdvancements.Save();
        Assert.NotNull(saved["probe:progress"]!["criteria"]!["one"]);
        Assert.Null(saved["probe:progress"]!["criteria"]!["two"]);
        container = (await manager.LoadPluginAsync(path))!;
        plugin = container.Plugin!;
        type = plugin.GetType();
        Assert.Equal(statId, (int)type.GetProperty("ProgressStatisticId")!.GetValue(plugin)!);
        trigger = (AdvancementTrigger<int>)type.GetProperty("ProgressTrigger")!.GetValue(plugin)!;
        await trigger.FireAsync(player, value => value == 8);
        Assert.True(player.Advancements.IsDone("probe:progress"));
        player.Statistics.Increment("probe:steps");
        Assert.Equal(5, player.Statistics.Get("probe:steps"));
        await manager.UnloadPluginAsync(container);
    }

    private static byte[] Read(string name)
    {
        using var stream = typeof(Progress).Assembly.GetManifestResourceStream($"Obsidian.Tests.Assets.{name}")!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
