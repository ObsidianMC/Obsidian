using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Tests.Fakes;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class BlockEntityLifting
{
    [Fact]
    public async Task PlayerSurfaceRiseCommitsUnderTickThenTransfer()
    {
        await using var context = TestServerFactory.CreateServer($"lift-player-locks-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        var initial = new VectorD(8.5, 64.875, 8.5);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Standing", initial);
        await player.UpdatePositionAsync(initial, MovementFlags.OnGround);
        Assert.True(world.TryAddPlayer(player));
        var correction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = Assert.IsType<TestClient>(player.Client);
        client.AfterQueue = packet =>
        {
            if (packet is PlayerPositionPacket)
            {
                Assert.True(world.Fluids.TickLock.IsHeldByCurrentThread);
                Assert.True(world.TransferLock.IsHeldByCurrentThread);
                correction.SetResult();
            }

            return ValueTask.CompletedTask;
        };

        lock (world.Fluids.TickLock)
        {
            world.LiftEntitiesForSurfaceRise(new Vector(8, 64, 8), 0.875, 1);
        }

        Assert.True(correction.Task.IsCompletedSuccessfully, "The synchronous lift did not queue a correction.");
        client.AfterQueue = null;
        Assert.Equal(new VectorD(8.5, 65, 8.5), player.Position);
        Assert.Equal(65, player.BoundingBox.Min.Y);
    }

    [Fact]
    public async Task SurfaceRiseCannotChangeAnEntityThatTeleportsDuringGeometryPreparation()
    {
        await using var source = TestServerFactory.CreateServer($"lift-source-{Guid.NewGuid():N}");
        await using var destination = TestServerFactory.CreateServer($"lift-destination-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(source.World);
        WorldBlockEntities.AddEmptyChunk(destination.World);
        var initial = new VectorD(8.5, 64.875, 8.5);
        var arrival = new VectorD(4.5, 80, 4.5);
        var pig = new TravellingPig { Level = source.World, Position = initial };
        pig.InitializeAi(false);
        Assert.True(source.World.TryAddEntity(pig));

        var travelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pig.BeforeBoxRead = () =>
        {
            // The virtual getter must run outside transfer. The traveller finishes before it returns the old box.
            Assert.False(source.World.TransferLock.IsHeldByCurrentThread);
            var travel = Task.Factory.StartNew(async () =>
            {
                await pig.TeleportAsync(destination.World, arrival);
                travelled.SetResult();
            }, default, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
            travel.GetAwaiter().GetResult();
        };

        lock (source.World.Fluids.TickLock)
        {
            source.World.LiftEntitiesForSurfaceRise(new Vector(8, 64, 8), 0.875, 1);
        }

        Assert.True(travelled.Task.IsCompletedSuccessfully, "The synchronous lift did not prepare the entity's box.");
        Assert.Same(destination.World, pig.Level);
        Assert.Equal(arrival, pig.Position);
        var expectedBox = pig.Dimension.CreateBBFromPosition(arrival);
        Assert.Equal(expectedBox.Min, pig.BoundingBox.Min);
        Assert.Equal(expectedBox.Max, pig.BoundingBox.Max);
        Assert.DoesNotContain(pig, source.World.EntitiesIn(initial, initial + new VectorD(1)));
        Assert.Contains(pig, destination.World.EntitiesIn(arrival, arrival + new VectorD(1)));
    }

    private sealed class TravellingPig : Mob
    {
        public TravellingPig()
        {
            this.Type = EntityType.Pig;
            this.Dimension = new EntityDimension { Width = 0.9f, Height = 0.9f };
        }

        protected override bool UsesAi => true;

        public Action? BeforeBoxRead { get; set; }

        public override BoundingBox BoundingBox
        {
            get
            {
                var box = base.BoundingBox;
                var before = this.BeforeBoxRead;
                this.BeforeBoxRead = null;
                before?.Invoke();
                return box;
            }
            protected set => base.BoundingBox = value;
        }
    }
}
