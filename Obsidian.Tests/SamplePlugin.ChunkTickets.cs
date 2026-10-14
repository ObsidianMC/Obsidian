using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Utilities;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed partial class SamplePlugin
{
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ChunkTicketsSimulateWithoutPlayersAndReleaseOnPluginUnload()
    {
        await using var context = TestServerFactory.CreateServer($"tickets-{Guid.NewGuid():N}");
        var world = context.World;
        world.LevelData.GameRules["spawn_mobs"] = "false";
        await using var manager = BlockUpdates.CreatePluginManager(context);
        var plugin = (await manager.LoadPluginAsync(await this.PackSampleAsync()))!;
        const int center = 40;

        IDisposable Acquire(int radius, bool ticking) => (IDisposable)plugin.Plugin!.GetType()
            .GetMethod("KeepChunks")!
            .Invoke(plugin.Plugin, [world, center, center, radius, ticking])!;

        // A loaded-only ticket generates and retains the square, but cannot run the pending machine tick.
        using var loaded = Acquire(1, ticking: false);
        await WaitForTicketChunksAsync(world, center, center, 1);
        Assert.Empty(world.Players);
        Assert.True(BlocksRegistry.TryGetPluginBlock("sample:tick_counter", out var counter));
        var position = new Vector(center * 16 + 4, 70, center * 16 + 4);
        var chunk = world.GetLoadedChunk(center, center)!;
        await world.SetBlockAsync(position, counter.DefaultState, BlockUpdateFlags.All);
        await world.SetBlockAsync(position + Vector.West, BlocksRegistry.RedstoneBlock, BlockUpdateFlags.All);
        await world.SetBlockAsync(position + Vector.East, BlocksRegistry.Wheat);
        await world.SetBlockAsync(position + Vector.South, BlocksRegistry.Water, BlockUpdateFlags.All);
        var item = new ItemEntity
        {
            EntityId = Server.GetNextEntityId(),
            Level = world,
            Position = (VectorD)position + new VectorD(4, 5, 4),
            Item = new ItemStack(ItemsRegistry.Stone, 1)
        };
        Assert.True(world.TryAddEntity(item));
        var height = item.Position.Y;
        var randomBefore = world.RandomTickValue;
        for (var tick = 0; tick < 6; tick++)
            await world.DoWorldTickAsync();

        var machine = chunk.GetBlockEntity(position.X, position.Y, position.Z)!;
        Assert.Equal(0, machine.GetType().GetProperty("Ticks")!.GetValue(machine));
        Assert.Equal("false", counter.GetValue(chunk.GetBlock(position), "powered"));
        Assert.Equal(randomBefore, world.RandomTickValue);
        Assert.Equal(height, item.Position.Y);
        Assert.True(chunk.GetBlock(position + Vector.South + Vector.Down).IsAir);

        using var ticking = Acquire(0, ticking: true);
        for (var tick = 0; tick < 6; tick++)
            await world.DoWorldTickAsync();

        Assert.Equal("true", counter.GetValue(chunk.GetBlock(position), "powered"));
        Assert.True(Ticks(chunk, position) > 0);
        Assert.NotEqual(randomBefore, world.RandomTickValue);
        Assert.True(item.Position.Y < height);
        Assert.False(chunk.GetBlock(position + Vector.South + Vector.Down).IsAir);
        Assert.False(world.IsMobTicking(new VectorD((center + 1) * 16 + 8, 0, center * 16 + 8)));

        world.LevelData.Time = 600;
        await world.ManageChunksAsync();
        await world.UnloadRegionAsync(center >> 5, center >> 5);
        Assert.Same(chunk, world.GetLoadedChunk(center, center));

        // Disposing one overlapping handle keeps the other. Plugin unload releases forgotten handles too.
        ticking.Dispose();
        var counted = Ticks(chunk, position);
        await world.DoWorldTickAsync();
        Assert.Equal(counted, Ticks(chunk, position));
        using var forgotten = Acquire(0, ticking: true);
        await manager.UnloadPluginAsync(plugin);
        world.LevelData.Time = 1200;
        await world.ManageChunksAsync();
        Assert.Null(world.GetLoadedChunk(center, center));
        Assert.DoesNotContain(NumericsHelper.IntsToLong(center, center), world.LoadedChunks);
        await world.UnloadRegionAsync(center >> 5, center >> 5);
        Assert.Null(world.GetRegionForChunk(center, center));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ChunkTicketRadiusOverlapAndLimitsReleaseWithHandles()
    {
        await using var context = TestServerFactory.CreateServer($"ticket-limits-{Guid.NewGuid():N}");
        var world = context.World;
        using var radius = world.AddChunkTicket(-40, -40, radius: 1);
        using var overlap = world.AddChunkTicket(-39, -39);
        await WaitForTicketChunksAsync(world, -40, -40, 2);
        Assert.True(world.IsMobTicking(new VectorD(-39 * 16 + 8, 0, -39 * 16 + 8)));
        Assert.False(world.IsMobTicking(new VectorD(-38 * 16 + 8, 0, -38 * 16 + 8)));
        radius.Dispose();
        world.LevelData.Time = 600;
        await world.ManageChunksAsync();
        Assert.Null(world.GetLoadedChunk(-42, -42));
        Assert.NotNull(world.GetLoadedChunk(-39, -39));
        overlap.Dispose();
        overlap.Dispose();
        world.LevelData.Time = 1200;
        await world.ManageChunksAsync();
        Assert.Null(world.GetLoadedChunk(-39, -39));

        Assert.Throws<ArgumentOutOfRangeException>(() => world.AddChunkTicket(0, 0, radius: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.AddChunkTicket(0, 0, radius: 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.AddChunkTicket(int.MaxValue, 0));
        var handles = new List<IDisposable>();
        try
        {
            for (var index = 0; index < 256; index++)
                handles.Add(world.AddChunkTicket(100, 100, ticking: false));

            Assert.Throws<InvalidOperationException>(() => world.AddChunkTicket(100, 100, ticking: false));
        }
        finally
        {
            foreach (var handle in handles)
                handle.Dispose();
        }

        using var first = world.AddChunkTicket(100, 100, radius: 8);
        using var second = world.AddChunkTicket(200, 200, radius: 8);
        Assert.Throws<InvalidOperationException>(() => world.AddChunkTicket(300, 300, radius: 8));
        first.Dispose();
        using var replacement = world.AddChunkTicket(300, 300, radius: 8);
    }

    private static async Task WaitForTicketChunksAsync(AbstractLevel level, int centerX, int centerZ, int radius)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            await level.ManageChunksAsync();
            var ready = true;
            for (var x = centerX - radius; x <= centerX + radius; x++)
            {
                for (var z = centerZ - radius; z <= centerZ + radius; z++)
                    ready &= level.GetLoadedChunk(x, z) is not null
                        && level.LoadedChunks.Contains(NumericsHelper.IntsToLong(x, z));
            }

            if (ready)
                return;

            Assert.True(DateTime.UtcNow < deadline, "Ticketed chunks did not finish loading.");
            await Task.Delay(10);
        }
    }
}
