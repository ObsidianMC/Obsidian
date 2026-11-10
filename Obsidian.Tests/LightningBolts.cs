using System;
using System.Linq;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// What vanilla can't check for lightning bolts: a bolt outliving its chunk, and the order a bolt's tick takes locks in.
/// The bolt's behaviour itself is replayed from vanilla by <see cref="VanillaLightning"/>.
/// </summary>
public sealed class LightningBolts
{
    private static readonly Vector Rod = new(8, BlockUpdates.Ground + 1, 8);

    /// <summary>
    /// A bolt whose chunk unloads mid-strike goes with it: the level's later ticks run without it, nothing is written
    /// into the unloaded chunk, and the chunk's save holds no bolt, as vanilla never saves one. The rod it powered keeps
    /// its power and the tick that ends it.
    /// </summary>
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task BoltWhoseChunkUnloadsIsDroppedWithoutBeingSaved()
    {
        await using var context = TestServerFactory.CreateServer($"lightning-unload-{Guid.NewGuid():N}");
        var world = context.World;
        await BlockUpdates.AddChunksAsync(world);
        using (world.AddChunkTicket(0, 0))
        {
            world.BlockUpdates.SetBlock(Rod, BlocksRegistry.LightningRod);
            world.SpawnEntity(Bolt(world));

            // The strike powers the rod; then the chunk goes, with the bolt still lit.
            await world.DoWorldTickAsync();
            Assert.Equal("true", world.BlockUpdates.GetBlock(Rod).GetProperty("powered"));

            var region = Assert.IsType<Region>(world.LoadRegionByChunk(0, 0));
            await region.UnloadChunk(0, 0);
            for (var tick = 0; tick < 5; tick++)
                await world.DoWorldTickAsync();

            Assert.Empty(Bolts(world));
        }

        // Loaded again from its save: no bolt, and the rod still powered until its tick comes.
        await world.GetChunkAsync(0, 0, scheduleGeneration: false);
        Assert.Empty(Bolts(world));
        Assert.Equal("true", world.BlockUpdates.GetBlock(Rod).GetProperty("powered"));
    }

    /// <summary>
    /// A bolt striking a player in armour wears the armour, which waits for the player's inventory: it does so outside
    /// the level's tick lock, so a flow holding that inventory can still change a block meanwhile, and the level's tick
    /// finishes once the inventory is let go.
    /// </summary>
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task BoltWaitsForAHeldInventoryOutsideTheTickLock()
    {
        await using var context = TestServerFactory.CreateServer($"lightning-locks-{Guid.NewGuid():N}");
        var world = context.World;
        await BlockUpdates.AddChunksAsync(world);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Struck",
            (VectorD)Rod + new VectorD(1.5, 0, 0.5));
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);
        player.GameMode = GameMode.Survival;
        var chestplate = new ItemStack(ItemsRegistry.IronChestplate);
        player.Inventory.SetItem(6, chestplate);
        world.LevelData.Difficulty = Difficulty.Peaceful;
        world.SpawnEntity(Bolt(world));

        // A flow holds the inventory, then changes a block, which takes the level's tick lock.
        var held = new TaskCompletionSource();
        var holder = Task.Run(async () =>
        {
            using (await player.Inventory.LockAsync())
            {
                held.SetResult();
                await Task.Delay(200);
                world.BlockUpdates.SetBlock(Rod + new Vector(0, 3, 0), BlocksRegistry.Stone);
            }
        });
        await held.Task;

        // The strike waits for the inventory to wear the chestplate, holding no lock the flow needs.
        await world.DoWorldTickAsync();
        await holder;

        Assert.True(world.BlockUpdates.GetBlock(Rod + new Vector(0, 3, 0)).Is(BlockIds.Stone));
        Assert.True(chestplate.Damage > 0);
        Assert.True(player.Health < 20);
    }

    private static LightningBolt Bolt(AbstractLevel level) => new(new LegacyRandomSource(1))
    {
        Level = level,
        EntityId = Server.GetNextEntityId(),
        Position = (VectorD)Rod + new VectorD(0.5, 1, 0.5)
    };

    private static LightningBolt[] Bolts(AbstractLevel level) =>
        level.GetNonPlayerEntitiesInRange((VectorD)Rod, 64).OfType<LightningBolt>().ToArray();
}
