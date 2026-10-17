using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class ServerPlayerTests
{
    [Fact]
    public async Task ServerPlayerLifecycleReachesConnectedHumans()
    {
        await using var context = TestServerFactory.CreateServer($"server-player-{Guid.NewGuid():N}");

        var human = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Human");
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, human);

        var client = (TestClient)human.Client;
        client.ClearPackets();

        var player = (ServerPlayer)await context.Server.AddServerPlayerAsync(
            Guid.NewGuid(), "ServerBot", context.World);
        player.Position = human.Position;

        Assert.True(ContainsPlayerInfo(client, player.Uuid));
        Assert.True(ContainsListedFlag(client, player.Uuid, listed: true));
        Assert.True(ContainsSpawn(client, player.EntityId));

        Assert.IsNotAssignableFrom<IClientPlayer>(player);
        Assert.Null(player.ClientIP);
        Assert.Same(player, context.Server.GetPlayer(player.Uuid));
        Assert.Same(player, context.Server.GetPlayer(player.Username));
        Assert.Same(player, context.World.Players[player.Uuid]);
        Assert.Contains(player, context.World.GetEntitiesInRange(player.Position, 1));
        await context.World.DoWorldTickAsync();

        var late = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "LateHuman", player.Position);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, late);
        var lateClient = (TestClient)late.Client;
        Assert.True(ContainsPlayerInfo(lateClient, player.Uuid));
        Assert.True(ContainsSpawn(lateClient, player.EntityId));

        client.ClearPackets();
        await player.SetGamemodeAsync(GameMode.Creative);
        Assert.Contains(client.SentPackets.OfType<PlayerInfoUpdatePacket>(), packet =>
            packet.Players.TryGetValue(player.Uuid, out var actions)
            && actions.OfType<UpdateGamemodeInfoAction>().Any());

        // Gameplay takes server-side players too: what they drop counts in their statistics, and creative adds to
        // their reach.
        Assert.True(ItemEntity.Drop(player, new ItemStack(ItemsRegistry.Get("minecraft:stone"), 3)));
        Assert.Equal(3, player.Statistics.Get("minecraft:stone", "minecraft:dropped"));
        Assert.Equal(5, Entities.Player.BlockInteractionRange(player));

        await ((Obsidian.WorldData.AbstractLevel)context.World).ExplodeAsync(player, 1f);
        Assert.Contains(client.SentPackets, packet => packet is ExplodePacket);

        client.ClearPackets();
        Assert.True(await context.Server.RemoveServerPlayerAsync(player));
        Assert.True(ContainsPlayerInfoRemoval(client, player.Uuid));
        Assert.True(ContainsEntityRemoval(client, player.EntityId));
        Assert.False(context.Server.IsPlayerOnline(player.Uuid));
        Assert.False(context.World.Players.ContainsKey(player.Uuid));
    }

    [Fact]
    public async Task ConnectedPlayerJoinBehavior_RemainsIntact()
    {
        await using var context = TestServerFactory.CreateServer($"connected-player-{Guid.NewGuid():N}");

        var first = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "First");
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, first);
        var firstClient = (TestClient)first.Client;
        firstClient.ClearPackets();

        var second = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Second", first.Position);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, second);
        var secondClient = (TestClient)second.Client;

        Assert.True(ContainsPlayerInfo(firstClient, second.Uuid));
        Assert.True(ContainsSpawn(firstClient, second.EntityId));
        Assert.True(ContainsPlayerInfo(secondClient, first.Uuid));
        Assert.True(ContainsSpawn(secondClient, first.EntityId));
    }

    [Fact]
    public async Task FailedAndStaleServerPlayerOperationsPreserveRegistrations()
    {
        {
            await using var context = TestServerFactory.CreateServer($"server-player-{Guid.NewGuid():N}");

            var existing = await context.Server.AddServerPlayerAsync(Guid.NewGuid(), "ServerBot", context.World);

            // Same UUID under a new name, then the same name under a new UUID.
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => context.Server.AddServerPlayerAsync(existing.Uuid, "OtherBot", context.World));
            var otherUuid = Guid.NewGuid();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => context.Server.AddServerPlayerAsync(otherUuid, "ServerBot", context.World));

            Assert.False(context.Server.IsPlayerOnline("OtherBot"));
            Assert.False(context.Server.IsPlayerOnline(otherUuid));
            Assert.Same(existing, context.Server.GetPlayer(existing.Uuid));
            Assert.Same(existing, context.Server.GetPlayer("ServerBot"));
        }

        {
            await using var context = TestServerFactory.CreateServer($"server-player-{Guid.NewGuid():N}");

            context.EventDispatcher.RegisterEvent(
                null,
                (Func<PlayerJoinEventArgs, ValueTask>)(_ => throw new InvalidOperationException("boom")),
                Priority.Critical);

            var uuid = Guid.NewGuid();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => context.Server.AddServerPlayerAsync(uuid, "BrokenBot", context.World));

            Assert.False(context.Server.IsPlayerOnline(uuid));
            Assert.False(context.World.Players.ContainsKey(uuid));
            Assert.Null(context.Server.GetPlayer(uuid));
        }

        {
            await using var context = TestServerFactory.CreateServer($"server-player-{Guid.NewGuid():N}");

            var uuid = Guid.NewGuid();
            var stale = await context.Server.AddServerPlayerAsync(uuid, "ServerBot", context.World);
            Assert.True(await context.Server.RemoveServerPlayerAsync(stale));
            var replacement = await context.Server.AddServerPlayerAsync(uuid, "ServerBot", context.World);

            Assert.False(await context.Server.RemoveServerPlayerAsync(stale));
            Assert.False(context.Server.RemovePlayer(stale));

            Assert.Same(replacement, context.Server.GetPlayer(uuid));
            Assert.Same(replacement, context.Server.GetPlayer("ServerBot"));
            Assert.Same(replacement, context.World.Players[uuid]);
        }
    }

    [Fact]
    public async Task FailingLeaveHandler_StillDespawnsServerPlayer()
    {
        await using var context = TestServerFactory.CreateServer($"server-player-{Guid.NewGuid():N}");

        var human = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Human");
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, human);
        var client = (TestClient)human.Client;

        var player = await context.Server.AddServerPlayerAsync(Guid.NewGuid(), "ServerBot", context.World);
        // Runs before the internal leave handler and stops it from running.
        context.EventDispatcher.RegisterEvent(
            null,
            (Func<PlayerLeaveEventArgs, ValueTask>)(_ => throw new InvalidOperationException("boom")),
            Priority.Critical);
        client.ClearPackets();

        Assert.True(await context.Server.RemoveServerPlayerAsync(player));

        Assert.True(ContainsPlayerInfoRemoval(client, player.Uuid));
        Assert.True(ContainsEntityRemoval(client, player.EntityId));
        Assert.False(context.Server.IsPlayerOnline(player.Uuid));
        Assert.False(context.World.Players.ContainsKey(player.Uuid));
        Assert.DoesNotContain(
            context.World.GetEntitiesInRange(player.Position, 1f), entity => entity.EntityId == player.EntityId);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ServerPlayerTakesPartInSurvivalCombatPickupAndRiding()
    {
        await using var context = TestServerFactory.CreateServer($"server-player-{Guid.NewGuid():N}");
        // An empty plains chunk, complete, so that its items, orbs and mobs tick.
        var chunk = new Chunk(0, 0);
        for (var x = 0; x < 16; x += 4)
        {
            for (var z = 0; z < 16; z += 4)
            {
                for (var y = -64; y < 320; y += 4)
                    chunk.SetBiome(x, y, z, CodecRegistry.Biomes.Plains);
            }
        }

        chunk.SetChunkStatus(ChunkGenStage.full);
        context.World.LoadRegionByChunk(0, 0).SetChunk(chunk);
        context.World.LevelData.Difficulty = Difficulty.Normal;

        var player = (ServerPlayer)await context.Server.AddServerPlayerAsync(Guid.NewGuid(), "ServerBot", context.World);
        var attacker = (ServerPlayer)await context.Server.AddServerPlayerAsync(Guid.NewGuid(), "Attacker", context.World);
        await player.SetGamemodeAsync(GameMode.Survival);
        await attacker.SetGamemodeAsync(GameMode.Survival);
        var position = new VectorD(8, 70, 8);
        await player.TeleportAsync(position);
        await attacker.TeleportAsync(position + new VectorD(1, 0, 0));

        // Survival: exhaustion past 4 costs a hunger point once saturation is gone, and a fed player regenerates.
        player.FoodSaturationLevel = 0;
        player.FoodExhaustionLevel = 4.5f;
        await context.World.DoWorldTickAsync();
        Assert.Equal(19, player.FoodLevel);

        player.FoodLevel = 20;
        player.FoodSaturationLevel = 5;
        player.Health = 10;
        for (var tick = 0; tick < 10; tick++)
            await context.World.DoWorldTickAsync();
        Assert.True(player.Health > 10);

        // Combat: another server-side player's attack, raised like a client's (see InteractPacket) and dealt on the
        // level's tick, can kill them, which drops what they carry, and they respawn.
        player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.Coal, 2));
        player.Health = 2;
        attacker.Inventory.SetItem(attacker.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.DiamondSword));
        Assert.True(attacker.CanAttack(player));
        var attack = new PlayerAttackEntityEventArgs(attacker, player, context.Server, sneaking: false);
        (attack.Damage, attack.IsCrit) = attacker.GetAttackDamage(player);
        await context.EventDispatcher.ExecuteEventAsync(attack);
        await context.World.DoWorldTickAsync();
        Assert.False(player.Alive);
        Assert.Null(player.Inventory.GetItem(9));
        Assert.Contains(context.World.GetNonPlayerEntitiesInRange(player.Position, 4).OfType<ItemEntity>(),
            drop => drop.Item.Is(ItemsRegistry.Coal));

        await player.RespawnAsync();
        Assert.True(player.Alive);
        Assert.Equal(20, player.Health);

        // Pickup: items and experience next to them are collected after the levels tick. The attacker leaves first, so
        // that they don't collect them.
        Assert.True(await context.Server.RemoveServerPlayerAsync(attacker));
        await player.TeleportAsync(position);
        var item = new ItemEntity
        {
            Level = context.World,
            EntityId = Server.GetNextEntityId(),
            Position = player.Position,
            Item = new ItemStack(ItemsRegistry.Diamond, 3),
            CanPickup = true
        };
        Assert.True(context.World.TryAddEntity(item));
        var orb = new ExperienceOrb
        {
            Level = context.World,
            EntityId = Server.GetNextEntityId(),
            Position = player.Position,
            Value = 7
        };
        Assert.True(context.World.TryAddEntity(orb));
        await context.World.DoWorldTickAsync();
        Assert.Contains(player.Inventory, stack => stack?.Is(ItemsRegistry.Diamond) == true && stack.Count == 3);
        Assert.True(item.Removed);
        Assert.Equal(7, player.XpTotal);
        Assert.DoesNotContain(context.World.GetNonPlayerEntitiesInRange(position, 4), entity => ReferenceEquals(entity, orb));

        // Riding: using a saddle on a pig saddles it, and using the pig again boards it, like a client's interactions.
        var pig = new Pig { Level = context.World, EntityId = Server.GetNextEntityId(), Position = position };
        pig.InitializeAi(false);
        Assert.True(context.World.TryAddEntity(pig));
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Saddle));
        await context.EventDispatcher.ExecuteEventAsync(
            new EntityInteractEventArgs(player, pig, context.Server, InteractionHand.MainHand));
        await context.World.DoWorldTickAsync();
        Assert.True(pig.HasSaddle);

        await context.EventDispatcher.ExecuteEventAsync(
            new EntityInteractEventArgs(player, pig, context.Server, InteractionHand.MainHand));
        await context.World.DoWorldTickAsync();
        Assert.Same(pig, player.Vehicle);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task RemovalFromOwnTick_LeavesAfterTheTick()
    {
        await using var context = TestServerFactory.CreateServer($"server-player-{Guid.NewGuid():N}");
        var player = (ServerPlayer)await context.Server.AddServerPlayerAsync(Guid.NewGuid(), "ServerBot", context.World);
        var runTick = typeof(PlayerBase).GetMethod("RunTickAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Task<IPlayer>? replacing = null;

        // As a plugin might from the player's tick: the leave waits for the tick, so adding the name again from the
        // tick fails, and from elsewhere waits for the leave.
        await (ValueTask)runTick.Invoke(player, [(Func<ValueTask>)(async () =>
        {
            Assert.True(await context.Server.RemoveServerPlayerAsync(player));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => context.Server.AddServerPlayerAsync(Guid.NewGuid(), "ServerBot", context.World));

            using (ExecutionContext.SuppressFlow())
                replacing = Task.Run(() => context.Server.AddServerPlayerAsync(Guid.NewGuid(), "ServerBot", context.World));
        }), false])!;

        var replacement = await replacing!;
        Assert.Same(replacement, context.Server.GetPlayer("ServerBot"));
        Assert.False(context.World.Players.ContainsKey(player.Uuid));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PermissionsFollowTheWholePath()
    {
        await using var context = TestServerFactory.CreateServer($"server-player-{Guid.NewGuid():N}");
        var player = await context.Server.AddServerPlayerAsync(Guid.NewGuid(), "ServerBot", context.World);

        Assert.True(await player.GrantPermissionAsync("obsidian.weather"));
        Assert.True(await player.GrantPermissionAsync("obsidian.time"));
        Assert.True(player.HasPermission("obsidian.weather"));
        Assert.False(player.HasPermission("obsidian.gamemode"));
        Assert.False(player.HasPermission("other.node"));

        // Revoking a node keeps its siblings.
        Assert.True(await player.RevokePermissionAsync("obsidian.weather"));
        Assert.False(player.HasPermission("obsidian.weather"));
        Assert.True(player.HasPermission("obsidian.time"));

        Assert.True(await player.GrantPermissionAsync("admin.*"));
        Assert.True(player.HasPermission("admin.anything.below"));
    }

    private static bool ContainsPlayerInfo(TestClient client, Guid uuid) =>
        client.SentPackets.OfType<PlayerInfoUpdatePacket>().Any(packet => packet.Players.ContainsKey(uuid));

    private static bool ContainsListedFlag(TestClient client, Guid uuid, bool listed) =>
        client.SentPackets.OfType<PlayerInfoUpdatePacket>()
            .Where(packet => packet.Players.ContainsKey(uuid))
            .SelectMany(packet => packet.Players[uuid])
            .OfType<UpdateListedInfoAction>()
            .Any(action => action.Listed == listed);

    private static bool ContainsPlayerInfoRemoval(TestClient client, Guid uuid) =>
        client.SentPackets.OfType<PlayerInfoRemovePacket>().Any(packet => packet.UUIDs.Contains(uuid));

    private static bool ContainsSpawn(TestClient client, int entityId) =>
        client.SentPackets.Any(packet => packet is AddEntityPacket add && add.EntityId == entityId
            || packet is BundledPacket bundle && bundle.Packets.OfType<AddEntityPacket>(
                ).Any(add => add.EntityId == entityId));

    private static bool ContainsEntityRemoval(TestClient client, int entityId) =>
        client.SentPackets.OfType<RemoveEntitiesPacket>().Any(packet => packet.Entities.Contains(entityId));
}
