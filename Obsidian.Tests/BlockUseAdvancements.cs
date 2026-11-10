using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

[Collection(nameof(PluginTagFiles))]
public sealed class BlockUseAdvancements
{
    [Fact]
    public async Task AnyBlockUseDoesNotReportLootAwardedByTheEarlierTrigger()
    {
        await using var context = TestServerFactory.CreateServer($"use-rewards-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        var target = new Vector(8, 64, 8);
        await world.GetBlockAsync(target);
        world.BlockUpdates.SetBlock(target, BlocksRegistry.Jukebox);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "User",
            new VectorD(8.5, 64, 10.5));
        context.World.TryAddPlayer(player);
        player.GameMode = GameMode.Survival;
        player.Inventory.SetItem(player.CurrentHeldItemSlot,
            new ItemStack(ItemsRegistry.Get("minecraft:music_disc_cat"), 1));

        using var reward = JsonDocument.Parse("""
            {
              "criteria": { "use": { "trigger": "minecraft:item_used_on_block" } },
              "rewards": { "loot": ["probe:use_reward"] }
            }
            """);
        using var any = JsonDocument.Parse("""
            {
              "criteria": {
                "reward": {
                  "trigger": "minecraft:any_block_use",
                  "conditions": {
                    "location": [{ "condition": "minecraft:match_tool", "predicate": { "items": "minecraft:diamond" } }]
                  }
                }
              }
            }
            """);
        var previous = AdvancementsRegistry.Current.Values.ToArray();
        try
        {
            AdvancementsRegistry.Apply(previous.Concat(
            [
                AdvancementsRegistry.Parse("probe:use_reward", reward.RootElement),
                AdvancementsRegistry.Parse("probe:any_reward", any.RootElement)
            ]));
            PluginLootTables.Apply([LootTableJson.Parse("""
                { "pools": [{ "rolls": 1, "entries": [{ "type": "minecraft:item", "name": "minecraft:diamond" }] }] }
                """, "probe:use_reward")]);

            var packet = BlockUpdates.UseOn(target, BlockFace.Up, new VectorF(0.5f, 0.5f, 0.5f));
            await packet.UseAsync(context.Server, player);

            Assert.True(player.Advancements.IsDone("probe:use_reward"));
            Assert.Equal(ItemsRegistry.Diamond.Id, player.GetHeldItem()?.Holder.Id);
            Assert.False(player.Advancements.HasCriterion("probe:any_reward", "reward"));
        }
        finally
        {
            AdvancementsRegistry.Apply(previous);
            PluginLootTables.Apply([]);
        }
    }
}
