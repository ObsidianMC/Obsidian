using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Loot;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

[Collection(nameof(PluginTagFiles))]
public sealed class VillagerLootContexts
{
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task GameplayLootResolvesVillagerVariants()
    {
        (string EntityType, string Source)[] cases =
        [
            ("villager", "entity"),
            ("zombie_villager", "entity"),
            ("villager", "block"),
            ("zombie_villager", "block"),
            ("villager", "container"),
            ("zombie_villager", "container"),
            ("villager", "vehicle"),
            ("zombie_villager", "vehicle")
        ];

        await FixtureCases.AssertAllAsync(
            cases,
            row => $"entityType={row.EntityType}, source={row.Source}",
            row => AssertGameplayLootResolvesVillagerVariants(row.EntityType, row.Source));
    }

    private static async Task AssertGameplayLootResolvesVillagerVariants(string entityType, string source)
    {
        await using var context = TestServerFactory.CreateServer($"variant-loot-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        using var stream = typeof(Server).Assembly.GetManifestResourceStream(
            "Obsidian.Assets.merchant-predicates-26.3.json")!;
        using var captured = JsonDocument.Parse(stream);
        Mob entity = entityType == "villager"
            ? new Villager { Level = context.World, VillagerType = 2 }
            : new ZombieVillager { Level = context.World, VillagerType = 2 };

        var rows = captured.RootElement.EnumerateArray()
            .Where(row => row.TryGetProperty("entityType", out var type) && type.GetString() == entityType).ToArray();
        Assert.NotEmpty(rows);

        try
        {
            foreach (var row in rows)
            {
                var table = LootTableJson.Parse($$"""
                    {"pools":[{"rolls":1,"entries":[{"type":"minecraft:item","name":"minecraft:emerald",
                      "condition":{{row.GetProperty("predicate").GetRawText()}}}]}]}
                    """, "minecraft:blocks/stone");
                PluginLootTables.Apply([table]);

                var count = Generate(source, context.World, entity, table);

                Assert.Equal(row.GetProperty("result").GetBoolean() ? 1 : 0, count);
            }
        }
        finally
        {
            PluginLootTables.Apply([]);
        }
    }

    private static int Generate(string source, World world, Mob entity, LootTable table)
    {
        switch (source)
        {
            case "entity":
                return table.GetRandomItems(entity.CreateEntityLootContext(table, null, null)).Count;
            case "block":
                return BlockLoot.GetDrops(BlocksRegistry.Stone, null, table.CreateRandom(1), entity,
                    checkTool: false).Count;
            case "container":
                var container = new ContainerBlockEntity(new Container(9))
                {
                    LootTable = table.Identifier,
                    LootTableSeed = 1
                };
                world.UnpackLootTable(container, entity);
                return container.Inventory.Sum(stack => stack?.Count ?? 0);
            case "vehicle":
                var vehicle = new VehicleInventory(9, "Probe")
                {
                    LootTable = table.Identifier,
                    LootTableSeed = 1
                };
                world.UnpackVehicleLoot(vehicle, entity, VectorD.Zero);
                return vehicle.Items.Sum(stack => stack?.Count ?? 0);
            default:
                throw new InvalidOperationException(source);
        }
    }
}
