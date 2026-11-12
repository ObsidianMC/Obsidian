using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Configuration;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Each level guards its items with its own transfer lock, and travel hands entities from one level to another, so that
/// levels can tick on threads of their own. These check that no item or entity is duplicated or lost doing so.
/// </summary>
public sealed partial class LevelTransfers
{
    private const int ItemCount = 120;
    private const int OverlappingHandoffs = 10;
    private const int MaxRounds = 400;

    // Items this far east are in reach of the level's hopper.
    private const double HopperX = 10;

    private static readonly VectorD Centre = new(8, 70, 8);

    // A place in the first region whose chunk isn't generated until a test adds it.
    private const int UngeneratedChunk = 2;
    private static readonly VectorD Ungenerated = new(UngeneratedChunk * 16 + 8, 70, UngeneratedChunk * 16 + 8);
    private static readonly VectorD HopperMin = new(HopperX, 60, 0);
    private static readonly VectorD HopperMax = new(16, 80, 16);
    private static readonly Vector HopperBlock = new(12, 69, 4);

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ItemsSentThroughDuringParallelTicksAreNeitherDuplicatedNorLost()
    {
        await using var firstServer = TestServerFactory.CreateServer($"transfers-first-{Guid.NewGuid():N}");
        await using var secondServer = TestServerFactory.CreateServer($"transfers-second-{Guid.NewGuid():N}");
        var first = await PrepareAsync(firstServer, "First");
        var second = await PrepareAsync(secondServer, "Second");
        for (var index = 0; index < ItemCount; index++)
            Drop(first.World, index, Spot(index));

        // Each level ticks on a thread of its own, as a server's levels will. During its tick, half its items go
        // through to the other level, as through a portal; after it, its player picks up and its hopper pulls.
        var traffic = new Traffic();
        await Task.WhenAll(
            Task.Run(() => RunLevelAsync(first, second, traffic)),
            Task.Run(() => RunLevelAsync(second, first, traffic)))
            .WaitAsync(TimeSpan.FromSeconds(25));

        // Every handoff succeeded (nothing else touches a level's items in its tick), and enough of them arrived while
        // the destination was ticking.
        Assert.Equal(0, traffic.Failed);
        Assert.True(traffic.Overlapping >= OverlappingHandoffs, $"Only {traffic.Overlapping} overlapping handoffs.");
        AssertEveryDiamondOnce(first, second);

        // Holding two levels' transfer locks at once could deadlock levels ticking in parallel, so it's refused.
        using (first.World.TransferLock.Enter())
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                using var both = second.World.TransferLock.Enter();
            });
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task HoppersAndMobsTakingTheSameItemsAtOnceTakeEachOnce()
    {
        const int count = 300;
        await using var server = TestServerFactory.CreateServer($"transfers-contended-{Guid.NewGuid():N}");
        var world = server.World;
        WorldBlockEntities.AddEmptyChunk(world);
        var items = Enumerable.Range(0, count)
            .Select(index => Drop(world, index, new VectorD(12.5, 70, 1.5 + index % 13)))
            .ToList();
        var hopper = new ItemAccessOver(new Container(27));
        var mob = new List<ItemStack>();

        // A hopper and a mob pull each item at the same moment: only the lock of the item's level keeps them from both
        // taking it.
        using var gate = new Barrier(2);
        await Task.WhenAll(
            OnOwnThread(() => ForEachTogether(items, gate, item => Pull(hopper, item, world))),
            OnOwnThread(() => ForEachTogether(items, gate, item => Equip(mob, item, world))))
            .WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(count, Diamonds(hopper.Items) + Diamonds(mob));
        Assert.Empty(ItemsIn(world));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AnItemThatLeftItsLevelCannotBeTakenThroughAnEarlierLookup()
    {
        await using var sourceServer = TestServerFactory.CreateServer($"stale-source-{Guid.NewGuid():N}");
        await using var targetServer = TestServerFactory.CreateServer($"stale-target-{Guid.NewGuid():N}");
        var source = sourceServer.World;
        var target = targetServer.World;
        WorldBlockEntities.AddEmptyChunk(source);
        WorldBlockEntities.AddEmptyChunk(target);
        var item = Drop(source, 0, new VectorD(12.5, 70, 4.5));

        // A mob and a hopper of the source found the item, which then goes through to the target.
        await item.TeleportAsync(target, new VectorD(12.5, 70, 4.5));

        // What they found is gone from their level: neither takes it, and the target's hopper takes it whole.
        var sourceHopper = new ItemAccessOver(new Container(5));
        var targetHopper = new ItemAccessOver(new Container(5));
        Assert.Null(item.TryTake(source, 1, _ => true));
        Assert.False(await Hoppers.TakeInAsync(sourceHopper.Access, item, source, HopperMin, HopperMax, HopperBlock));
        Assert.True(await Hoppers.TakeInAsync(targetHopper.Access, item, target, HopperMin, HopperMax, HopperBlock));
        Assert.Equal(1, Diamonds(targetHopper.Items));
        Assert.Equal(0, Diamonds(sourceHopper.Items));

        // Removed from its level (as a removal or an unload does, without marking it removed), an item found before
        // gives nothing either.
        var removed = Drop(target, 1, new VectorD(12.5, 70, 4.5));
        Assert.True(await target.DestroyEntityAsync(removed));
        Assert.False(removed.Removed);
        Assert.Null(removed.TryTake(target, 1, _ => true));
        Assert.False(await Hoppers.TakeInAsync(targetHopper.Access, removed, target, HopperMin, HopperMax, HopperBlock));
        Assert.Equal(1, Diamonds(targetHopper.Items));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PluginCodeShowingAnArrivalCanSendAnotherEntityOn()
    {
        await using var firstServer = TestServerFactory.CreateServer($"relay-first-{Guid.NewGuid():N}");
        await using var secondServer = TestServerFactory.CreateServer($"relay-second-{Guid.NewGuid():N}");
        await using var thirdServer = TestServerFactory.CreateServer($"relay-third-{Guid.NewGuid():N}");
        var first = firstServer.World;
        var second = secondServer.World;
        var third = thirdServer.World;
        WorldBlockEntities.AddEmptyChunk(first);
        WorldBlockEntities.AddEmptyChunk(second);
        WorldBlockEntities.AddEmptyChunk(third);
        var passedOn = new Cow { Level = second, EntityId = 711_001, Position = Centre };
        Assert.True(second.TryAddEntity(passedOn));
        var relay = new RelayCow(passedOn, third) { Level = first, EntityId = 711_000, Position = Centre };
        Assert.True(first.TryAddEntity(relay));

        // Spawning the relay where it arrives, a plugin's override sends the other cow on from there at once.
        await relay.TeleportAsync(second, Centre);
        await relay.Relayed!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Same(third, passedOn.Level);
        Assert.Same(passedOn, third.GetRegionForLocation(Centre)!.Entities[passedOn.EntityId]);
        Assert.False(second.GetRegionForLocation(Centre)!.Entities.ContainsKey(passedOn.EntityId));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ATravellerNeitherLevelTakesInIsNeverLost()
    {
        await using var sourceServer = TestServerFactory.CreateServer($"refused-source-{Guid.NewGuid():N}");
        await using var targetServer = TestServerFactory.CreateServer($"refused-target-{Guid.NewGuid():N}");
        var source = sourceServer.World;
        var target = targetServer.World;
        const int chunk = 64;
        var position = new VectorD(chunk * 16 + 8.5, 70, chunk * 16 + 8.5);
        WorldBlockEntities.AddEmptyChunk(source, chunk, chunk);
        WorldBlockEntities.AddEmptyChunk(target);
        await source.GetChunkAsync(chunk, chunk, scheduleGeneration: false);
        var item = Drop(source, 0, position);

        // The target already has an entity with the item's UUID, so it refuses the item. While the item waits to get
        // in, the source unloads the chunks nobody needs: the one the item left is kept for its way back.
        Assert.True(target.TryAddEntity(Twin(item, target, Centre)));
        source.LevelData.GameTime = 20 * 30;
        var refused = await TravelWhileTargetBusyAsync(item, target, Centre, source.ManageChunksAsync);
        Assert.IsType<InvalidOperationException>(refused);
        AssertBackInChunk(item, source, position, chunk);

        // Nor does its region retire meanwhile.
        refused = await TravelWhileTargetBusyAsync(item, target, Centre, () =>
            source.UnloadRegionAsync(chunk >> Region.CubicRegionSizeShift, chunk >> Region.CubicRegionSizeShift));
        Assert.IsType<InvalidOperationException>(refused);
        AssertBackInChunk(item, source, position, chunk);

        // Refused by the source too (an entity with its UUID came meanwhile), it's kept in the chunk it left, to be
        // saved and spawned again, rather than dropped.
        refused = await TravelWhileTargetBusyAsync(item, target, Centre, () =>
        {
            Assert.True(source.TryAddEntity(Twin(item, source, position)));
            return Task.CompletedTask;
        });
        Assert.IsType<InvalidOperationException>(refused);
        Assert.Null(item.TryTake(source, 1, _ => true));

        // While the entity with its UUID stays, the kept item isn't dropped by the source's next tick either.
        await source.DoWorldTickAsync();
        Assert.Contains(LoadedChunk(source, chunk)!.UnspawnableEntities, IsItem);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task TravelPublishesTheEntityAndItsPassengersAtOnce()
    {
        await using var sourceServer = TestServerFactory.CreateServer($"publish-source-{Guid.NewGuid():N}");
        await using var targetServer = TestServerFactory.CreateServer($"publish-target-{Guid.NewGuid():N}");
        var source = sourceServer.World;
        var target = targetServer.World;
        WorldBlockEntities.AddEmptyChunk(source);
        WorldBlockEntities.AddEmptyChunk(target);
        var watcher = TestServerFactory.CreateConnectedPlayer(targetServer.Server, target, "Watcher", Centre);
        await TestServerFactory.JoinConnectedPlayerAsync(targetServer.Server, watcher);
        target.TryAddPlayer(watcher);
        target.TryAddEntity(watcher);
        watcher.LoadedChunks.Add(0);
        var vehicle = new Cow { Level = source, EntityId = 713_000, Position = Centre };
        var rider = new Chicken { Level = source, EntityId = 713_001, Position = Centre + VectorD.Up };
        Assert.True(source.TryAddEntity(vehicle));
        Assert.True(source.TryAddEntity(rider));
        Assert.True(rider.Ride(vehicle));

        // Once the teleport returns, the target finds both by UUID, and its players saw the vehicle spawn before any
        // seating of it, like vanilla's teleport, which adds the entity to its new level at once.
        await vehicle.TeleportAsync(target, Centre);
        var packets = ((TestClient)watcher.Client).SentPackets.ToList();
        Assert.Same(vehicle, target.FindEntity(vehicle.Uuid));
        Assert.Same(rider, target.FindEntity(rider.Uuid));
        Assert.Same(vehicle, rider.Vehicle);
        var spawned = packets.FindIndex(packet => packet is BundledPacket bundle
            && bundle.Packets.Any(part => part is AddEntityPacket { EntityId: 713_000 }));
        var seated = packets.FindIndex(packet =>
            packet is SetPassengersPacket { EntityId: 713_000 });
        Assert.True(spawned >= 0 && seated > spawned, $"Spawned at {spawned}, seated at {seated}.");

        // Spawning code that throws doesn't lose the entity: it's in the target already.
        var faulty = new ThrowingCow { Level = source, EntityId = 713_002, Position = Centre };
        Assert.True(source.TryAddEntity(faulty));
        await Assert.ThrowsAsync<NotSupportedException>(() => faulty.TeleportAsync(target, Centre).AsTask());
        Assert.Same(faulty, target.FindEntity(faulty.Uuid));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AnArrivalBeforeTheEntityPhaseIsTickedInThatTick()
    {
        await using var sourceServer = TestServerFactory.CreateServer($"held-source-{Guid.NewGuid():N}");
        await using var targetServer = TestServerFactory.CreateServer($"held-target-{Guid.NewGuid():N}");
        var source = sourceServer.World;
        var target = targetServer.World;
        WorldBlockEntities.AddEmptyChunk(source);
        WorldBlockEntities.AddEmptyChunk(target);
        var watcher = TestServerFactory.CreateConnectedPlayer(targetServer.Server, target, "Watcher", Centre);
        await TestServerFactory.JoinConnectedPlayerAsync(targetServer.Server, watcher);
        target.TryAddPlayer(watcher);
        target.TryAddEntity(watcher);
        var cow = new CountingCow { Level = source, EntityId = 714_000, Position = Centre };
        Assert.True(source.TryAddEntity(cow));

        // It arrives during the target's tick, before its entity phase: like vanilla, which adds it to the tick list
        // at once, the target ticks it in that same tick.
        target.EnqueueEntityAction(() => cow.TeleportAsync(target, Centre));
        await target.DoWorldTickAsync();
        Assert.Same(cow, target.FindEntity(cow.Uuid));
        Assert.Equal(1, cow.Ticks);

        await target.DoWorldTickAsync();
        Assert.Equal(2, cow.Ticks);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ATravellerIsNotTickedByItsNewLevelWhileItsOldLevelStillTicksIt()
    {
        await using var sourceServer = TestServerFactory.CreateServer($"claimed-source-{Guid.NewGuid():N}");
        await using var targetServer = TestServerFactory.CreateServer($"claimed-target-{Guid.NewGuid():N}");
        var source = sourceServer.World;
        var target = targetServer.World;
        await WatchAsync(sourceServer, source);
        await WatchAsync(targetServer, target);
        var cow = new GatedCow { Level = source, EntityId = 715_000, Position = Centre };
        Assert.True(source.TryAddEntity(cow));

        // The source's tick of the cow waits (as a plugin's tick awaiting something might) while a command sends the
        // cow to the target, which then ticks without it: the source's tick of it isn't over.
        var sourceTick = Task.Run(() => source.DoWorldTickAsync());
        await cow.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cow.TeleportAsync(target, Centre);
        Assert.Same(cow, target.FindEntity(cow.Uuid));
        await target.DoWorldTickAsync();
        Assert.Equal(1, cow.Ticks);

        // Once it is, the target ticks it.
        cow.Release.SetResult();
        await sourceTick.WaitAsync(TimeSpan.FromSeconds(10));
        await target.DoWorldTickAsync();
        Assert.Equal(2, cow.Ticks);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ATravellerLandingWhereItsDestinationUnloadsGoesBack()
    {
        await using var sourceServer = TestServerFactory.CreateServer($"closing-source-{Guid.NewGuid():N}");
        await using var targetServer = TestServerFactory.CreateServer($"closing-target-{Guid.NewGuid():N}");
        var source = sourceServer.World;
        var target = targetServer.World;
        WorldBlockEntities.AddEmptyChunk(source);
        WorldBlockEntities.AddEmptyChunk(target);
        var item = Drop(source, 0, Centre);

        // The target's region starts retiring (its save has taken its entities) while the item waits to get in.
        var refused = await TravelWhileTargetBusyAsync(
            item,
            target,
            Centre,
            meanwhile: () => Task.CompletedTask,
            underTargetLock: () => ((Region)target.GetRegionForLocation(Centre)!).TransfersClosed = true);

        Assert.IsType<InvalidOperationException>(refused);
        Assert.Same(source, item.Level);
        Assert.Same(item, source.GetRegionForLocation(Centre)!.Entities[item.EntityId]);
        Assert.False(target.GetRegionForLocation(Centre)!.Entities.ContainsKey(item.EntityId));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ATravellerWhoseAdmissionThrowsIsNeverLost()
    {
        await using var sourceServer = TestServerFactory.CreateServer($"throwing-source-{Guid.NewGuid():N}");
        await using var targetServer = TestServerFactory.CreateServer($"throwing-target-{Guid.NewGuid():N}");
        var source = sourceServer.World;
        var target = targetServer.World;
        WorldBlockEntities.AddEmptyChunk(source);
        WorldBlockEntities.AddEmptyChunk(target);
        var cow = new Cow { Level = source, EntityId = 716_000, Position = Centre };
        Assert.True(source.TryAddEntity(cow));
        var spawner = ClosingPlugin();
        try
        {
            // The plugin that spawned the cow is closing, so adding the cow to either level throws: it's kept in the
            // chunk it left rather than lost between them.
            cow.SpawnedBy = spawner;
            spawner.Close();
            Assert.NotNull(await Record.ExceptionAsync(() => cow.TeleportAsync(target, Centre).AsTask()));

            Assert.Contains(LoadedChunk(source, 0)!.PendingEntities, kept => kept.Type == "minecraft:cow");
            Assert.False(target.GetRegionForLocation(Centre)!.Entities.ContainsKey(cow.EntityId));
        }
        finally
        {
            spawner.UndoHostAdditions();
            await spawner.DisposeAsync();
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AnItemRemovedFromItsLevelDoesNotAbsorbAnother()
    {
        await using var server = TestServerFactory.CreateServer($"merge-{Guid.NewGuid():N}");
        var side = await PrepareAsync(server, "Merger");
        var world = side.World;
        var receiver = Drop(world, 0, Centre);
        var donor = Drop(world, 1, Centre);

        // Removed after its tick began (a removal doesn't mark it), the receiver must not take in the donor.
        Assert.True(await world.DestroyEntityAsync(receiver));
        for (var tick = 0; tick < 40; tick++)
            await receiver.TickAsync();

        Assert.Same(donor, world.GetRegionForLocation(Centre)!.Entities[donor.EntityId]);
        Assert.False(donor.Removed);
        Assert.Equal(1, donor.Item.Count);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task WhatAHopperCantHoldAnyMoreIsNeverLost()
    {
        await using var server = TestServerFactory.CreateServer($"overflow-{Guid.NewGuid():N}");
        var world = server.World;
        WorldBlockEntities.AddEmptyChunk(world);
        var item = Drop(world, 0, new VectorD(12.5, 70, 4.5));

        // The slots looked like they had room, but none by the time the item went in. It's dropped at the hopper,
        // which here is in a chunk that isn't loaded, so it's kept in the loaded chunk the item was in instead (saved
        // with it) and spawned there with the next tick.
        var hopperBlock = new Vector(20, 70, 4);
        Assert.True(await Hoppers.TakeInAsync(new ShrinkingSlots(), item, world, HopperMin, HopperMax, hopperBlock));
        Assert.Contains(LoadedChunk(world, 0)!.PendingEntities, kept => kept.Type == "minecraft:item");

        await world.DoWorldTickAsync();
        var dropped = Assert.Single(world.GetNonPlayerEntitiesInRange(Centre, 32).OfType<ItemEntity>());
        Assert.True(dropped.Item.Is(ItemsRegistry.Diamond));
    }

    private static async Task WatchAsync(RealServerContext context, World level)
    {
        WorldBlockEntities.AddEmptyChunk(level);
        var watcher = TestServerFactory.CreateConnectedPlayer(context.Server, level, "Watcher", Centre);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, watcher);
        level.TryAddPlayer(watcher);
        level.TryAddEntity(watcher);
    }

    private static PluginContainer ClosingPlugin() => new()
    {
        Info = new PluginInfo("transfers"),
        Archive = PluginArchive.Open("transfers.obby", PluginArchives.Zip(new Dictionary<string, byte[]>
        {
            [PluginMetadata.FileName] = PluginArchives.Metadata("transfers", "1.0.0", "Transfers")
        }))
    };

    private static bool IsItem(NbtCompound kept) =>
        kept.TryGetTagValue<string>("id", out var id) && id == "minecraft:item";

    private static void AssertBackInChunk(ItemEntity item, World source, VectorD position, int chunk)
    {
        Assert.Same(source, item.Level);
        Assert.Same(item, source.GetRegionForLocation(position)!.Entities[item.EntityId]);
        Assert.NotNull(LoadedChunk(source, chunk));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AnEntityRemovedWhileTravellingNeverArrives()
    {
        await using var sourceServer = TestServerFactory.CreateServer($"transit-source-{Guid.NewGuid():N}");
        await using var targetServer = TestServerFactory.CreateServer($"transit-target-{Guid.NewGuid():N}");
        var source = sourceServer.World;
        var target = targetServer.World;
        WorldBlockEntities.AddEmptyChunk(source);
        WorldBlockEntities.AddEmptyChunk(target);

        // An item, and an orb, which unlike items and mobs has no removed flag of its own to stop it.
        Entity[] travellers =
        [
            Drop(source, 0, Centre),
            new ExperienceOrb { Level = source, EntityId = 712_000, Position = Centre }
        ];
        Assert.True(source.TryAddEntity(travellers[1]));

        foreach (var traveller in travellers)
        {
            // The target is busy when the entity gets there, and removes it before letting it in.
            var removal = Task.FromResult(false);
            var refused = await TravelWhileTargetBusyAsync(
                traveller,
                target,
                Centre,
                meanwhile: () => Task.CompletedTask,
                underTargetLock: () => removal = target.DestroyEntityAsync(traveller).AsTask());

            Assert.True(removal.IsCompletedSuccessfully);
            Assert.True(await removal);
            Assert.IsType<InvalidOperationException>(refused);
            Assert.DoesNotContain(traveller, source.GetNonPlayerEntitiesInRange(Centre, 8));
            Assert.DoesNotContain(traveller, target.GetNonPlayerEntitiesInRange(Centre, 8));
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task APlayerRespawningInAnotherLevelIsTickedByNeitherUntilTheRespawnIsOver()
    {
        await using var context = TestServerFactory.CreateServer($"respawn-{Guid.NewGuid():N}");
        var world = context.World;
        var nether = AddNether(context);
        WorldBlockEntities.AddEmptyChunk(world);
        AddEmptyChunk(nether);
        world.LevelData.SpawnPosition = Centre;
        var player = await DeadPlayerAsync(context, nether, "Respawner");

        // With no respawn point, the player dying in the nether respawns at the world spawn. The overworld is busy as
        // the player gets there, so the respawn waits between leaving the nether and joining the overworld.
        using var busy = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = OnOwnThread(() =>
        {
            using (world.TransferLock.Enter())
            {
                busy.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }
        });
        busy.Wait(TimeSpan.FromSeconds(10));
        var respawn = Task.Run(() => player.RespawnAsync());
        try
        {
            // Meanwhile the overworld owns the player, the nether no longer finds it, and neither level may tick it.
            Assert.True(SpinWait.SpinUntil(() => player.Level == world, TimeSpan.FromSeconds(10)));
            Assert.Null(nether.FindEntity(player.Uuid));
            Assert.False(nether.Players.ContainsKey(player.Uuid));
            Assert.False(player.TryClaimTick(world));
            Assert.False(player.TryClaimTick(nether));
        }
        finally
        {
            release.Set();
        }

        await holder.WaitAsync(TimeSpan.FromSeconds(10));
        await respawn.WaitAsync(TimeSpan.FromSeconds(10));

        // Once the respawn is over, the player is one of the overworld's players and entities, and the overworld's
        // alone, which may tick it from then on.
        Assert.True(player.Alive);
        Assert.True(IsInRegion(world, player));
        Assert.Same(player, world.Players[player.Uuid]);
        Assert.Null(nether.FindEntity(player.Uuid));
        Assert.False(nether.Players.ContainsKey(player.Uuid));
        Assert.True(player.TryClaimTick(world));
        player.ReleaseTick(world);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ARespawnAwaitedInItsLevelsTickDoesNotWaitForItsChunkToGenerate()
    {
        await using var context = TestServerFactory.CreateServer($"respawn-ungenerated-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        world.LevelData.SpawnPosition = Ungenerated;
        var player = await DeadPlayerAsync(context, world, "Waiter");

        // A plugin awaits the respawn inside the level's tick, which chunk management, and so generation, waits for.
        // The world spawn's chunk isn't generated, so the player waits in the level, among none of its entities
        // or players, so not even the pass after the levels' ticks simulates it.
        world.EnqueueEntityAction(async () => await player.RespawnAsync());
        await world.DoWorldTickAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(player.Alive);
        Assert.Same(world, player.Level);
        Assert.False(IsInRegion(world, player));
        Assert.False(world.Players.ContainsKey(player.Uuid));

        // A tick before the chunk is ready leaves the player waiting; the tick after it's ready takes the player in.
        await world.DoWorldTickAsync();
        Assert.False(IsInRegion(world, player));

        WorldBlockEntities.AddEmptyChunk(world, UngeneratedChunk, UngeneratedChunk);
        await world.DoWorldTickAsync();

        Assert.True(IsInRegion(world, player));
        Assert.Same(player, world.Players[player.Uuid]);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ATeleportAwaitedInItsLevelsTickGeneratesItsDestination()
    {
        await using var context = TestServerFactory.CreateServer($"teleport-ungenerated-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        var cow = new Cow { Level = world, EntityId = 718_000, Position = Centre };
        Assert.True(world.TryAddEntity(cow));

        // A plugin awaits a teleport inside the level's tick, to a chunk that isn't generated. Chunk management may be
        // waiting for that tick to unload a chunk, so it starts no generation here: the teleport has to.
        world.EnqueueEntityAction(async () => await cow.TeleportAsync(world, Ungenerated));
        await world.DoWorldTickAsync().WaitAsync(TimeSpan.FromSeconds(10));

        // Like a vanilla portal's, the destination was generated before the cow arrived.
        Assert.True(world.GetLoadedChunk(UngeneratedChunk, UngeneratedChunk) is { IsGenerated: true });
        Assert.Equal(Ungenerated, cow.Position);
        Assert.Same(cow, world.FindEntity(cow.Uuid));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task APlayerThatLeftBeforeItsRespawnMovedItIsTakenInNowhere()
    {
        await using var context = TestServerFactory.CreateServer($"respawn-left-{Guid.NewGuid():N}");
        var world = context.World;
        var nether = AddNether(context);
        WorldBlockEntities.AddEmptyChunk(world);
        AddEmptyChunk(nether);
        world.LevelData.SpawnPosition = Centre;
        var player = await DeadPlayerAsync(context, nether, "Leaver");

        // The player leaves while its respawn is still on its way (loading its chunk, say), before it left the nether.
        Assert.True(Entity.LeaveServer(player));
        Assert.True(await nether.DestroyEntityAsync(player));
        await player.RespawnAsync();
        await world.DoWorldTickAsync();

        // It stays where it left, and neither level has it among its players or entities.
        Assert.Same(nether, player.Level);
        Assert.False(world.Players.ContainsKey(player.Uuid));
        Assert.False(IsInRegion(world, player));
        Assert.False(nether.Players.ContainsKey(player.Uuid));
        Assert.Null(nether.FindEntity(player.Uuid));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task APlayerTeleportedWhileWaitingToArriveIsTakenInWhereItWent()
    {
        await using var context = TestServerFactory.CreateServer($"respawn-teleport-{Guid.NewGuid():N}");
        var world = context.World;
        var nether = AddNether(context);
        WorldBlockEntities.AddEmptyChunk(world);
        AddEmptyChunk(nether);
        world.LevelData.SpawnPosition = Ungenerated;
        var player = await DeadPlayerAsync(context, nether, "Jumper");

        // Dying in the nether, the player respawns at the world spawn, whose chunk isn't generated: it waits in the
        // overworld, among none of its entities or players.
        await player.RespawnAsync();
        Assert.Same(world, player.Level);
        Assert.False(IsInRegion(world, player));
        Assert.False(world.Players.ContainsKey(player.Uuid));

        // Sending it to another dimension meanwhile is refused, not dropped while reporting success.
        Assert.False(await player.TeleportToDimensionAsync(nether.DimensionName));
        Assert.Same(world, player.Level);

        // Teleported meanwhile to a loaded chunk of another region, it's taken in there.
        WorldBlockEntities.AddEmptyChunk(world, Region.CubicRegionSize, 0);
        var elsewhere = new VectorD(Region.CubicRegionSize * 16 + 8, 70, 8);
        await player.TeleportAsync(world, elsewhere);

        Assert.Equal(elsewhere, player.Position);
        Assert.True(IsInRegion(world, player));
        Assert.Same(player, world.Players[player.Uuid]);
        Assert.False(world.LoadRegionByChunk(0, 0).Entities.ContainsKey(player.EntityId));

        // The overworld's retry finds nothing left to do, and the overworld alone may tick the player.
        await world.DoWorldTickAsync();
        Assert.True(IsInRegion(world, player));
        Assert.Same(player, world.Players[player.Uuid]);
        Assert.True(player.TryClaimTick(world));
        player.ReleaseTick(world);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task APlayerUnregisteredAndRegisteredAgainStillTravelsAndRespawns()
    {
        await using var context = TestServerFactory.CreateServer($"reregistered-{Guid.NewGuid():N}");
        var world = context.World;
        var nether = AddNether(context);
        WorldBlockEntities.AddEmptyChunk(world);
        AddEmptyChunk(nether);
        world.LevelData.SpawnPosition = Centre;
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Returner", Centre);
        Assert.True(context.Server.AddPlayer(player));
        Assert.True(world.TryAddPlayer(player));
        Assert.True(world.TryAddEntity(player));

        // A plugin removes the still-connected player's registration and adds it again.
        Assert.True(context.Server.RemovePlayer(player));
        Assert.True(context.Server.AddPlayer(player));

        // The player still travels to another level...
        await player.TeleportAsync(nether, Centre);
        Assert.Same(nether, player.Level);
        Assert.True(IsInRegion(nether, player));
        Assert.Same(player, nether.Players[player.Uuid]);

        // ...and, dying there, respawns at the world spawn.
        await player.KillAsync(player, "Returner died");
        await player.RespawnAsync();
        Assert.Same(world, player.Level);
        Assert.True(IsInRegion(world, player));
        Assert.Same(player, world.Players[player.Uuid]);
        Assert.False(nether.Players.ContainsKey(player.Uuid));
    }

    // A player of the server's, dead in level.
    private static async Task<Entities.Player> DeadPlayerAsync(RealServerContext context, AbstractLevel level, string name)
    {
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, name, Centre);
        player.Level = level;
        Assert.True(level.TryAddPlayer(player));
        Assert.True(level.TryAddEntity(player));
        await player.KillAsync(player, $"{name} died");
        return player;
    }

    // Whether the player is among the entities of the region at its position, which lookups by UUID don't show: they
    // find players among the level's players first.
    private static bool IsInRegion(AbstractLevel level, Entities.Player player) =>
        level.GetRegionForLocation(player.Position) is IRegion region
        && region.Entities.TryGetValue(player.EntityId, out var entity)
        && ReferenceEquals(entity, player);

    internal static WorldData.Dimension AddNether(RealServerContext context) =>
        AddDimension(context, "minecraft:the_nether");

    private static WorldData.Dimension AddDimension(RealServerContext context, string identifier)
    {
        var world = context.World;
        var dimension = new WorldData.Dimension(
            NullLogger<WorldData.Dimension>.Instance,
            context.Broadcaster,
            new TestOptionsMonitor<ServerConfiguration>(world.Configuration),
            context.EventDispatcher,
            new EmptyWorldGenerator(),
            identifier,
            world);
        dimension.Initialize(CodecRegistry.Dimensions.All[identifier]);
        world.RegisterDimension(dimension);
        return dimension;
    }

    internal static void AddEmptyChunk(AbstractLevel level) =>
        level.LoadRegionByChunk(0, 0).SetChunk(new Chunk(0, 0, level.MinY, level.Height, ChunkGenStage.full));

    // Teleports an entity while the target's transfer lock is held elsewhere. Once the entity has left its level and
    // waits to get in, runs meanwhile, then underTargetLock on the thread holding the target's lock, which then lets
    // the entity in. Returns what the teleport threw, if anything.
    private static async Task<Exception?> TravelWhileTargetBusyAsync(
        Entity traveller, World target, VectorD position, Func<Task> meanwhile, Action? underTargetLock = null)
    {
        using var busy = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = OnOwnThread(() =>
        {
            using (target.TransferLock.Enter())
            {
                busy.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                underTargetLock?.Invoke();
            }
        });
        busy.Wait(TimeSpan.FromSeconds(10));

        var travel = Task.Run(() => traveller.TeleportAsync(target, position).AsTask());
        try
        {
            Assert.True(SpinWait.SpinUntil(() => traveller.Level == target, TimeSpan.FromSeconds(10)));
            await meanwhile();
        }
        finally
        {
            release.Set();
        }

        await holder.WaitAsync(TimeSpan.FromSeconds(10));
        return await Record.ExceptionAsync(() => travel.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    private static ItemEntity Twin(ItemEntity item, World level, VectorD position)
    {
        var twin = new ItemEntity
        {
            Level = level,
            EntityId = item.EntityId + 1_000,
            Position = position,
            Item = new ItemStack(ItemsRegistry.Stone)
        };
        twin.Uuid = item.Uuid;
        return twin;
    }

    private static Chunk? LoadedChunk(World level, int chunk)
    {
        var local = chunk & (Region.CubicRegionSize - 1);
        var region = (Region)level.GetRegionForChunk(chunk, chunk)!;
        return region.GetLoadedChunk(local, local) as Chunk;
    }

    private sealed record Side(AbstractLevel World, Entities.Player Player, ItemAccessOver Hopper);

    // What the levels' threads did: handoffs that succeeded and failed, and those that left while the destination
    // was ticking.
    private sealed class Traffic
    {
        private int handedOff;
        private int failed;
        private int overlapping;

        public int Failed => Volatile.Read(ref this.failed);

        public int Overlapping => Volatile.Read(ref this.overlapping);

        public bool Enough => this.Overlapping >= OverlappingHandoffs && Volatile.Read(ref this.handedOff) >= ItemCount;

        public void Record(bool succeeded, bool destinationTicking)
        {
            if (!succeeded)
            {
                Interlocked.Increment(ref this.failed);
                return;
            }

            Interlocked.Increment(ref this.handedOff);
            if (destinationTicking)
                Interlocked.Increment(ref this.overlapping);
        }
    }

    // A floor of stone in the level's first chunk, with a player on it. The world's own level unless another is given.
    private static async Task<Side> PrepareAsync(RealServerContext context, string name, AbstractLevel? level = null)
    {
        var world = level ?? context.World;
        if (world is World own)
            WorldBlockEntities.AddEmptyChunk(own);
        else
            AddEmptyChunk(world);

        for (var x = 0; x < 16; x++)
        {
            for (var z = 0; z < 16; z++)
                await world.SetBlockAsync(new Vector(x, 69, z), BlocksRegistry.Stone);
        }

        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, name, new VectorD(3.5, 70, 3.5));
        player.Level = world;
        Assert.True(world.TryAddPlayer(player));
        Assert.True(world.TryAddEntity(player));

        return new Side(world, player, new ItemAccessOver(new Container(27)));
    }

    private static ItemEntity Drop(AbstractLevel world, int index, VectorD position)
    {
        var item = new ItemEntity
        {
            Level = world,
            EntityId = 700_000 + index,
            Position = position,
            Item = new ItemStack(ItemsRegistry.Diamond),
            CanPickup = true
        };
        Assert.True(world.TryAddEntity(item));
        return item;
    }

    // Spread over the floor, so some land by the player, some by the hopper and the rest nowhere in particular.
    private static VectorD Spot(int index) => new(1.5 + index % 13, 70, 1.5 + index / 13 % 13);

    private static async Task RunLevelAsync(Side side, Side other, Traffic traffic)
    {
        for (var round = 0; round < MaxRounds && !traffic.Enough; round++)
        {
            var parity = round % 2;
            side.World.EnqueueEntityAction(() => SendHalfThroughAsync(side.World, other.World, parity, traffic));
            await side.World.DoWorldTickAsync();
            await side.Player.PickupNearbyItemsAsync();

            foreach (var item in ItemsIn(side.World).Where(item => item.Position.X >= HopperX))
                await Hoppers.TakeInAsync(side.Hopper.Access, item, side.World, HopperMin, HopperMax, HopperBlock);
        }
    }

    // Run in the level's tick, as a portal sends entities through.
    private static async ValueTask SendHalfThroughAsync(
        AbstractLevel level, AbstractLevel destination, int parity, Traffic traffic)
    {
        foreach (var item in ItemsIn(level).Where(item => item.Position.X < HopperX && item.EntityId % 2 == parity))
        {
            var ticking = destination.CurrentTickId != 0;
            try
            {
                await item.TeleportAsync(destination, Spot(item.EntityId + parity));
                traffic.Record(succeeded: true, ticking);
            }
            catch (InvalidOperationException)
            {
                traffic.Record(succeeded: false, ticking);
            }
        }
    }

    private static void ForEachTogether(List<ItemEntity> items, Barrier gate, Action<ItemEntity> take)
    {
        foreach (var item in items)
        {
            Assert.True(gate.SignalAndWait(TimeSpan.FromSeconds(10)));
            take(item);
        }
    }

    private static void Pull(ItemAccessOver hopper, ItemEntity item, World world) =>
        Hoppers.TakeInAsync(hopper.Access, item, world, HopperMin, HopperMax, HopperBlock)
            .AsTask()
            .GetAwaiter()
            .GetResult();

    // What a mob picking up its equipment does.
    private static void Equip(List<ItemStack> equipped, ItemEntity item, AbstractLevel world)
    {
        if (item.TryTake(world, 1, _ => true) is not { } taken)
            return;

        equipped.Add(taken.Stack);
        if (taken.Emptied)
            item.RemoveAsync().AsTask().GetAwaiter().GetResult();
    }

    private static List<ItemEntity> ItemsIn(AbstractLevel world) =>
        world.GetNonPlayerEntitiesInRange(Centre, 32).OfType<ItemEntity>().ToList();

    private static void AssertEveryDiamondOnce(params Side[] sides) => AssertEveryDiamondOnce(ItemCount, sides);

    private static void AssertEveryDiamondOnce(int expected, params Side[] sides)
    {
        var entities = new HashSet<ItemEntity>(ReferenceEqualityComparer.Instance);
        var diamonds = 0;
        foreach (var side in sides)
        {
            var resident = side.World.Regions.Values
                .SelectMany(region => region.Entities.Values)
                .OfType<ItemEntity>();
            foreach (var item in resident)
            {
                Assert.True(entities.Add(item), $"Item {item.EntityId} is in two places.");
                Assert.Same(side.World, item.Level);
                Assert.False(item.Removed);
                diamonds += item.Item.Count;
            }

            diamonds += Diamonds(side.Player.Inventory);
            diamonds += Diamonds(side.Hopper.Items);
        }

        Assert.Equal(expected, diamonds);
    }

    private static int Diamonds(IEnumerable<ItemStack> stacks) =>
        stacks.Where(stack => stack is not null && stack.Is(ItemsRegistry.Diamond)).Sum(stack => stack.Count);

    private static Task OnOwnThread(Action work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    // A cow whose spawning, like a plugin entity's override might, sends another entity on to a third level at once.
    private sealed class RelayCow(Entity next, World onwards) : Cow
    {
        public Task? Relayed { get; private set; }

        public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0)
        {
            base.SpawnEntity(velocity, additionalData);
            this.Relayed ??= next.TeleportAsync(onwards, Centre).AsTask();
        }
    }

    // A cow whose first tick waits for the test to release it, as a plugin's tick awaiting something might.
    private sealed class GatedCow : Cow
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Ticks { get; private set; }

        public override async ValueTask TickAsync()
        {
            this.Ticks++;
            if (this.Entered.TrySetResult())
                await this.Release.Task;

            await base.TickAsync();
        }
    }

    private sealed class CountingCow : Cow
    {
        public int Ticks { get; private set; }

        public override ValueTask TickAsync()
        {
            this.Ticks++;
            return base.TickAsync();
        }
    }

    // A cow whose spawning, like a plugin entity's override might, throws.
    private sealed class ThrowingCow : Cow
    {
        public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0) =>
            throw new NotSupportedException("Spawning failed.");
    }

    // One slot that says it has room, then has none when filled.
    private sealed class ShrinkingSlots : IItemAccess
    {
        public int SlotCount => 1;

        public ItemStack? GetItem(int slot) => null;

        public ItemStack? Insert(int slot, ItemStack stack, bool simulate = false) => simulate ? null : stack;

        public ItemStack? Extract(int slot, int count, bool simulate = false) => null;
    }

    // A hopper's slots, reached as a hopper reaches the container it fills.
    private sealed class ItemAccessOver(Container items)
    {
        private readonly object gate = new();

        public Container Items => items;

        public IItemAccess Access => new Slots(this);

        private sealed class Slots(ItemAccessOver owner) : IItemAccess
        {
            public int SlotCount => owner.Items.Size;

            public ItemStack? GetItem(int slot) => owner.Items.GetItem(slot);

            public ItemStack? Insert(int slot, ItemStack stack, bool simulate = false)
            {
                lock (owner.gate)
                {
                    var held = owner.Items.GetItem(slot);
                    if (held is not null && held != stack)
                        return stack;

                    var moved = Math.Min(stack.MaxStackSize - (held?.Count ?? 0), stack.Count);
                    if (moved <= 0)
                        return stack;

                    if (!simulate)
                        owner.Items.SetItem(slot, new ItemStack(stack, (held?.Count ?? 0) + moved));

                    var left = stack.Count - moved;
                    return left > 0 ? new ItemStack(stack, left) : null;
                }
            }

            public ItemStack? Extract(int slot, int count, bool simulate = false) => null;
        }
    }
}
