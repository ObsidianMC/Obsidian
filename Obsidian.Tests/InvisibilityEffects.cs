using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Effects;
using Obsidian.Entities;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Vanilla's <c>LivingEntity.updateInvisibilityStatus</c>: the invisible flag follows the invisibility effect, and a
/// spectator player stays invisible whatever its effects are.
/// </summary>
public class InvisibilityEffects
{
    private const int InvisibilityId = (int)PotionEffect.Invisibility - 1;

    private static Pig SpawnPig(World world, VectorD? position = null)
    {
        var pig = new Pig
        {
            Level = world,
            EntityId = Obsidian.Server.GetNextEntityId(),
            Position = position ?? new VectorD(8, 64, 8),
            Type = EntityType.Pig
        };
        Assert.True(world.TryAddEntity(pig));
        return pig;
    }

    [Fact(DisplayName = "A mob's invisibility change is sent to the players watching it", Timeout = TestServerFactory.TestTimeout)]
    public async Task InvisibilityChangesAreSentToWatchers()
    {
        await using var context = TestServerFactory.CreateServer($"invisibility-watchers-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        var position = new VectorD(8, 64, 8);
        var viewer = TestServerFactory.CreateConnectedPlayer(
            context.Server, context.World, "InvisibilityWatcher", position);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, viewer);
        viewer.LoadedChunks.Add(0L);
        var pig = Assert.IsType<Pig>(context.World.SpawnEntity(position, EntityType.Pig));
        var client = (TestClient)viewer.Client;
        client.ClearPackets();

        pig.AddPotionEffect(InvisibilityId, 200);

        AssertFlagPacket(client, pig, invisible: true);

        client.ClearPackets();
        pig.RemovePotionEffect(InvisibilityId);

        AssertFlagPacket(client, pig, invisible: false);
    }

    internal static void AssertFlagPacket(TestClient client, Entity entity, bool invisible)
    {
        var packet = Assert.Single(client.SentPackets.OfType<SetEntityDataPacket>(),
            packet => packet.EntityId == entity.EntityId);
        using var buffer = new NetworkBuffer();
        packet.Serialize(buffer);
        buffer.Offset = 0;
        Assert.Equal(entity.EntityId, buffer.ReadVarInt());
        Assert.Equal(0, buffer.ReadByte());
        Assert.Equal((int)EntityMetadataType.Byte, buffer.ReadVarInt());
        Assert.Equal(invisible ? 0x20 : 0, buffer.ReadByte());
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ConcurrentRemovalCannotLeaveAnAddedEffectFlagStale()
    {
        await using var context = TestServerFactory.CreateServer($"invisibility-race-{Guid.NewGuid():N}");
        using var readingFlag = new ManualResetEventSlim();
        using var resumeAdd = new ManualResetEventSlim();
        using var removedEffect = new ManualResetEventSlim();
        using var resumeRemoval = new ManualResetEventSlim();
        var entity = new GatedLiving
        {
            Level = context.World,
            ReadingFlag = readingFlag,
            ResumeAdd = resumeAdd,
            RemovedEffect = removedEffect,
            ResumeRemoval = resumeRemoval
        };

        var add = Task.Run(() => entity.AddPotionEffect(InvisibilityId, 200));
        readingFlag.Wait();
        var remove = Task.Run(() => entity.RemovePotionEffect(InvisibilityId));
        removedEffect.Wait();
        resumeAdd.Set();
        await add;
        try
        {
            // Removal has committed to the dictionary but has not reconciled the flag yet.
            Assert.False(entity.HasPotionEffect(InvisibilityId));
            Assert.False(entity.Invisible);
        }
        finally
        {
            resumeRemoval.Set();
            await remove;
        }
        Assert.False(entity.Invisible);
    }

    private sealed class GatedLiving : Living
    {
        internal required ManualResetEventSlim ReadingFlag { get; init; }
        internal required ManualResetEventSlim ResumeAdd { get; init; }
        internal required ManualResetEventSlim RemovedEffect { get; init; }
        internal required ManualResetEventSlim ResumeRemoval { get; init; }
        private int reads;

        public override bool Invisible
        {
            get
            {
                var current = base.Invisible;
                if (Interlocked.Increment(ref this.reads) == 1)
                {
                    this.ReadingFlag.Set();
                    this.ResumeAdd.Wait();
                }
                return current;
            }
            set => base.Invisible = value;
        }

        internal override void UpdateInvisibilityStatus()
        {
            if (!this.HasPotionEffect(InvisibilityId))
            {
                this.RemovedEffect.Set();
                this.ResumeRemoval.Wait();
            }
            base.UpdateInvisibilityStatus();
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task RegionCrossingCannotLoseInvisibilityBroadcast()
    {
        await using var context = TestServerFactory.CreateServer($"invisibility-crossing-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World, chunkX: 31);
        WorldBlockEntities.AddEmptyChunk(context.World, chunkX: 32);
        var position = new VectorD(511.5, 64, 8);
        var destination = new VectorD(512.5, 64, 8);
        var viewer = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "CrossingWatcher", position);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, viewer);
        viewer.LoadedChunks.Add(31L);
        viewer.LoadedChunks.Add(32L);
        var pig = SpawnPig(context.World, position);
        var origin = Assert.IsType<Region>(context.World.GetRegionForLocation(position));
        var target = Assert.IsType<Region>(context.World.GetRegionForLocation(destination));
        var client = (TestClient)viewer.Client;
        client.ClearPackets();

        Assert.True(context.World.TryMoveEntity(pig, position, destination));
        Assert.False(origin.Entities.ContainsKey(pig.EntityId));
        Assert.True(target.Entities.ContainsKey(pig.EntityId));
        // The real movement caller assigns Position only after TryMoveEntity returns.
        pig.AddPotionEffect(InvisibilityId, 200);

        AssertFlagPacket(client, pig, invisible: true);
        pig.Position = destination;
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task WanderingTraderSendsOneVisibilityUpdatePerTransition()
    {
        await using var context = TestServerFactory.CreateServer($"invisibility-trader-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        var position = new VectorD(8, 64, 8);
        var viewer = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "TraderWatcher", position);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, viewer);
        viewer.LoadedChunks.Add(0L);
        var trader = Assert.IsType<WanderingTrader>(context.World.SpawnEntity(position, EntityType.WanderingTrader));
        var client = (TestClient)viewer.Client;

        foreach (var night in new[] { true, false })
        {
            context.World.DayTime = night ? 13000 : 1000;
            client.ClearPackets();
            await trader.TickAsync();

            AssertFlagPacket(client, trader, invisible: night);
        }
    }
}
