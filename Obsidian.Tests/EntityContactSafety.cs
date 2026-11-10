using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class EntityContactSafety
{
    [Fact]
    public async Task PlayerLandingKeepsItsSupportAcrossLaterMovement()
    {
        await using var context = TestServerFactory.CreateServer($"landing-support-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        await world.GetChunkAsync(0, 0, scheduleGeneration: false);
        world.GrowthRandom = new LegacyRandomSource(0);
        var cases = new[]
        {
            (X: 0, Landing: BlocksRegistry.Stone, Later: BlocksRegistry.SlimeBlock, Health: 13f),
            (X: 4, Landing: BlocksRegistry.SlimeBlock, Later: BlocksRegistry.Stone, Health: 20f),
            (X: 8, Landing: BlocksRegistry.Farmland, Later: BlocksRegistry.Stone, Health: 13f)
        };

        foreach (var test in cases)
        {
            var landedOn = new Vector(test.X, 64, 4);
            var walkedOnto = landedOn + new Vector(1, 0, 0);
            world.BlockUpdates.SetBlock(landedOn, test.Landing);
            world.BlockUpdates.SetBlock(walkedOnto, test.Later);
            var height = test.Landing.Is(BlockIds.Farmland) ? 64.9375 : 65;
            var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, $"Mover{test.X}",
                new VectorD(test.X + 0.5, height + 10, 4.5));
            await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);
            player.GameMode = GameMode.Survival;

            // Two accepted movement updates arrive before the level dispatches the landing hook.
            await player.UpdateAsync(new VectorD(test.X + 0.5, height, 4.5), MovementFlags.OnGround);
            var laterPosition = new VectorD(test.X + 1.5, 65, 4.5);
            await player.UpdateAsync(laterPosition, MovementFlags.OnGround);
            await world.EntityContacts.TouchAsync(player);

            Assert.Equal(test.Health, player.Health);
            Assert.Equal(laterPosition, player.Position);
            Assert.True(world.BlockUpdates.GetBlock(walkedOnto).IsSameState(test.Later));
            if (test.Landing.Is(BlockIds.Farmland))
                Assert.True(world.BlockUpdates.GetBlock(landedOn).Is(BlockIds.Dirt));
        }
    }

    [Fact]
    public async Task FarmlandContactDoesNotOverwriteAConcurrentReplacementOrLiftItsEntities()
    {
        await using var context = TestServerFactory.CreateServer($"trample-race-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        await world.GetChunkAsync(0, 0, scheduleGeneration: false);
        var position = new Vector(8, 64, 8);
        world.BlockUpdates.SetBlock(position, BlocksRegistry.Farmland);
        var standing = new VectorD(8.5, 64.9375, 8.5);
        var pig = new Pig { Level = world, Position = standing, MovementFlags = MovementFlags.OnGround };
        pig.InitializeAi(finalizeSpawn: false);
        world.SpawnEntity(pig);
        var item = new ItemEntity
        {
            Level = world,
            EntityId = Server.GetNextEntityId(),
            Position = standing,
            Item = new ItemStack(ItemsRegistry.Stone, 1)
        };
        world.SpawnEntity(item);
        pig.RecordLanding(2);

        // Replace after the contact read, before the trample commits. A dedicated thread avoids pool starvation.
        world.GrowthRandom = new ReplacementRandom(() =>
        {
            Assert.False(world.Fluids.TickLock.IsHeldByCurrentThread);
            var replacement = Task.Factory.StartNew(
                () => world.BlockUpdates.SetBlock(position, BlocksRegistry.DiamondBlock),
                default,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            Assert.True(replacement.Wait(TimeSpan.FromSeconds(10)), "Concurrent replacement was blocked.");
            replacement.GetAwaiter().GetResult();
        });
        await world.EntityContacts.TouchAsync(pig);

        Assert.True(world.BlockUpdates.GetBlock(position).Is(BlockIds.DiamondBlock));
        Assert.Equal(standing, pig.Position);
        Assert.Equal(standing, item.Position);
    }

    [Fact]
    public async Task TurtleEggContactDoesNotOverwriteAConcurrentReplacement()
    {
        await using var context = TestServerFactory.CreateServer($"egg-race-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        await world.GetChunkAsync(0, 0, scheduleGeneration: false);
        var position = new Vector(8, 64, 8);
        world.BlockUpdates.SetBlock(position + Vector.Down, BlocksRegistry.Sand);
        world.BlockUpdates.SetBlock(position, BlocksRegistry.TurtleEgg.WithProperty("eggs", "2"));
        var pig = new Pig
        {
            Level = world,
            Position = new VectorD(8.5, 64.4375, 8.5),
            MovementFlags = MovementFlags.OnGround
        };
        pig.InitializeAi(finalizeSpawn: false);
        world.SpawnEntity(pig);
        pig.RecordLanding(2);

        // Replace after the contact read, before the egg breaks; every draw then passes the break chance.
        world.GrowthRandom = new ReplacementRandom(() =>
        {
            Assert.False(world.Fluids.TickLock.IsHeldByCurrentThread);
            var replacement = Task.Factory.StartNew(
                () => world.BlockUpdates.SetBlock(position, BlocksRegistry.DiamondBlock),
                default,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            Assert.True(replacement.Wait(TimeSpan.FromSeconds(10)), "Concurrent replacement was blocked.");
            replacement.GetAwaiter().GetResult();
        }, alwaysZero: true);
        await world.EntityContacts.TouchAsync(pig);

        Assert.True(world.BlockUpdates.GetBlock(position).Is(BlockIds.DiamondBlock));
    }

    private sealed class ReplacementRandom(Action replace, bool alwaysZero = false) : LegacyRandomSource(0)
    {
        private bool replaced;

        public override int Next(int bits)
        {
            if (!this.replaced)
            {
                this.replaced = true;
                replace();
            }

            var drawn = base.Next(bits);
            return alwaysZero ? 0 : drawn;
        }
    }

    [Fact]
    public async Task CellsWithoutHooksAllocateNothing()
    {
        await using var level = TestWorldFactory.Create("empty-contact-allocation");
        var blocks = new RedstoneTestWorld();
        blocks.Load(0, 15, 62, 67, 0, 15);
        var entity = new ItemEntity
        {
            Level = level,
            Position = new VectorD(8.5, 64, 8.5),
            MovementFlags = MovementFlags.OnGround
        };
        var contacts = new EntityContactPass(blocks);
        for (var index = 0; index < 20_000; index++)
            contacts.TouchAsync(entity).GetAwaiter().GetResult();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 100_000; index++)
            contacts.TouchAsync(entity).GetAwaiter().GetResult();

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }
}
