using Obsidian.API;
using Obsidian.API.Data;
using Obsidian.Nbt;
using Obsidian.API.Registries;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Plugins;
using Obsidian.API.World;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Obsidian.Registries;
using Obsidian.WorldData.Portals;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public partial class Portals
{
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task FlintAndSteelUseOnFrameIgnitesNetherPortal()
    {
        await using var context = TestServerFactory.CreateServer($"portal-ignition-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        AddDimension(context, "minecraft:the_nether");
        foreach (var (position, block) in Frame("x", 2, 3))
            await world.SetBlockAsync(position, block);

        var player = TestServerFactory.CreateConnectedPlayer(
            context.Server, world, "PortalIgniter", new VectorD(0.5, 64, -2.5));
        player.GameMode = GameMode.Creative;
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.FlintAndSteel));
        Assert.True(world.TryAddPlayer(player));
        Assert.True(world.TryAddEntity(player));

        await BlockUpdates.UseOn(new Vector(0, 63, 0), BlockFace.Up).HandleAsync(context.Server, player);

        var frame = await PortalFrame.FindAsync(world.GetBlockAsync, new Vector(0, 64, 0), "x", world.MinY);
        Assert.True(frame!.Value.IsComplete);
        Assert.Contains(((TestClient)player.Client).SentPackets, packet => packet is BlockChangedAckPacket);
        Assert.Contains(((TestClient)player.Client).SentPackets, packet => packet is SoundPacket
        {
            SoundLocation: "minecraft:item.flintandsteel.use"
        });
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task LevelTeleportsKeepPlayerAndMobStateAndSendLevelChangePackets()
    {
        await using var source = TestServerFactory.CreateServer($"travel-source-{Guid.NewGuid():N}");
        await using var target = TestServerFactory.CreateServer($"travel-target-{Guid.NewGuid():N}");
        AddChunks(source.World);
        AddChunks(target.World);
        var player = TestServerFactory.CreateConnectedPlayer(source.Server, source.World, "Traveller");
        Assert.True(source.World.TryAddPlayer(player));
        Assert.True(source.World.TryAddEntity(player));
        player.Health = 13;
        player.FoodLevel = 7;
        player.Motion = new VectorD(0.2, 0.3, -0.4);
        player.Inventory[36] = new ItemStack(ItemsRegistry.Diamond, 3);
        var client = (TestClient)player.Client;
        client.ClearPackets();
        var arrival = new VectorD(8.25, 76, 9.75);

        await ((IEntity)player).TeleportAsync(target.World, arrival, 45f, -10f);

        Assert.Same(target.World, player.Level);
        Assert.Equal(arrival, player.Position);
        Assert.Equal(45f, player.Yaw.Degrees);
        Assert.Equal(((Angle)(-10f)).Degrees, player.Pitch.Degrees);
        Assert.Equal(13, player.Health);
        Assert.Equal(7, player.FoodLevel);
        Assert.Equal(3, player.Inventory[36]!.Count);
        Assert.Equal(new VectorD(0.2, 0.3, -0.4), player.Motion);
        Assert.False(source.World.Players.ContainsKey(player.Uuid));
        Assert.Same(player, target.World.Players[player.Uuid]);
        Assert.Same(player, target.World.GetRegionForLocation(arrival)!.Entities[player.EntityId]);
        var respawn = Assert.Single(client.SentPackets.OfType<RespawnPacket>());
        Assert.Equal(target.World.DimensionName, respawn.CommonPlayerSpawnInfo.DimensionName);
        Assert.Equal(DataKept.Attributes | DataKept.Metadata, respawn.DataKept);
        var move = Assert.Single(client.SentPackets.OfType<PlayerPositionPacket>());
        Assert.Equal(arrival, move.Position);
        Assert.Equal(player.Motion, move.Delta);
        var difficulty = Assert.Single(client.SentPackets.OfType<ChangeDifficultyPacket>());
        var packets = client.SentPackets.ToList();
        Assert.True(packets.IndexOf(respawn) < packets.IndexOf(difficulty));
        Assert.True(packets.IndexOf(difficulty) < packets.IndexOf(move));
        Assert.Single(client.SentPackets.OfType<SetExperiencePacket>());
        Assert.Equal((Vector)target.World.LevelData.SpawnPosition.Floor(),
            Assert.Single(client.SentPackets.OfType<SetDefaultSpawnPositionPacket>()).Position.Pos);

        var localArrival = new VectorD(-16.5, 70, 8.5);
        await ((IEntity)player).TeleportAsync(target.World, localArrival, 135f);
        Assert.Equal(localArrival, player.Position);
        Assert.Equal(135f, player.Yaw.Degrees);
        Assert.Equal(arrival, player.LastPosition);
        Assert.False(target.World.GetRegionForLocation(arrival)!.Entities.ContainsKey(player.EntityId));
        Assert.Same(player, target.World.GetRegionForLocation(localArrival)!.Entities[player.EntityId]);

        var mob = new TravelCow
        {
            Level = source.World,
            EntityId = 987654,
            Position = new VectorD(3.5, 64, 3.5),
            Health = 6,
            Motion = new VectorD(0.1, 0, 0.2),
            Type = EntityType.Cow
        };
        Assert.True(source.World.TryAddEntity(mob));
        var rider = TestServerFactory.CreateConnectedPlayer(
            source.Server, source.World, "Rider", new VectorD(3.5, 65, 3.5));
        Assert.True(source.World.TryAddPlayer(rider));
        Assert.True(source.World.TryAddEntity(rider));
        Assert.True(mob.AddRider(rider));

        // The whole passenger tree travels and boards again; a lead the vehicle holds breaks when it leaves.
        var jockey = new Cow
        {
            Level = source.World,
            EntityId = 987656,
            Position = new VectorD(3.5, 66, 3.5),
            Type = EntityType.Cow
        };
        Assert.True(source.World.TryAddEntity(jockey));
        Assert.False(jockey.Ride(mob));
        Assert.True(jockey.Ride(rider));
        var leashed = new Cow
        {
            Level = source.World,
            EntityId = 987657,
            Position = new VectorD(6.5, 64, 6.5),
            Type = EntityType.Cow
        };
        Assert.True(source.World.TryAddEntity(leashed));
        leashed.LeashTo(mob);
        var uuid = mob.Uuid;
        var attachments = AttachmentStorage.For(mob);
        attachments.Set("travelprobe:value", new NbtTag<int>("travelprobe:value", 1));
        await ((IEntity)mob).TeleportAsync(target.World, arrival, 90f, 5f);
        Assert.Same(mob, target.World.GetRegionForLocation(arrival)!.Entities[mob.EntityId]);
        Assert.False(source.World.GetRegionForLocation(new VectorD(3.5, 64, 3.5))!.Entities.ContainsKey(mob.EntityId));
        Assert.Equal(uuid, mob.Uuid);
        Assert.Same(target.World, rider.Level);
        Assert.Same(mob, rider.Vehicle);
        Assert.Equal(arrival + VectorD.Up, rider.Position);
        Assert.Same(target.World, jockey.Level);
        Assert.Equal(arrival + VectorD.Up * 2, jockey.Position);
        Assert.Equal(new Entity[] { rider }, mob.Passengers);
        Assert.Same(rider, jockey.Vehicle);
        leashed.TickLeash();
        Assert.Null(leashed.LeashHolder);
        Assert.Contains(
            source.World.GetEntitiesInRange(leashed.Position, 2).OfType<ItemEntity>(),
            item => item.Item!.Is(ItemsRegistry.Lead));
        Assert.Equal(6, mob.Health);
        Assert.Equal(arrival, mob.Position);
        Assert.Equal(90f, mob.Yaw.Degrees);
        Assert.Equal(new VectorD(0.1, 0, 0.2), mob.Motion);

        await mob.TeleportAsync(new VectorD(4.5, 72, 5.5));
        Assert.Equal(new VectorD(4.5, 72, 5.5), mob.Position);
        Assert.Equal(mob.Position + VectorD.Up * 2, jockey.Position);
        Assert.Same(rider, jockey.Vehicle);
        attachments.Set("travelprobe:value", new NbtTag<int>("travelprobe:value", 2));
        Assert.Equal(2, Assert.IsType<NbtTag<int>>(attachments.Get("travelprobe:value")).Value);

        // A destination that is still a proto chunk keeps the teleport pending until removal cancels it.
        source.World.LoadRegionByChunk(0, 0).SetChunk(
            new Chunk(0, 0, source.World.MinY, source.World.Height, ChunkGenStage.empty));
        var pending = mob.TeleportAsync(source.World, arrival).AsTask();
        Assert.False(pending.IsCompleted);
        await target.World.DestroyEntityAsync(mob);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.False(source.World.GetRegionForLocation(arrival)!.Entities.ContainsKey(mob.EntityId));
        Assert.False(target.World.GetRegionForLocation(mob.Position)!.Entities.ContainsKey(mob.EntityId));
        Assert.Throws<InvalidOperationException>(() =>
            attachments.Set("travelprobe:value", new NbtTag<int>("travelprobe:value", 3)));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PluginPortalLightsTravelsBuildsExitAndBecomesInertOnUnload()
    {
        await using var source = TestServerFactory.CreateServer($"portal-source-{Guid.NewGuid():N}");
        await using var target = TestServerFactory.CreateServer($"portal-target-{Guid.NewGuid():N}");
        AddChunks(source.World);
        AddChunks(target.World);
        var owner = new PluginContainer
        {
            Info = new PluginInfo("travelprobe"),
            Archive = PluginArchive.Open("travelprobe.obby", PluginArchives.Zip(new Dictionary<string, byte[]>
            {
                [PluginMetadata.FileName] = PluginArchives.Metadata("travelprobe", "1.0.0", "Travel probe")
            })),
            RegisteringWorldgenTypes = true
        };
        var blockProperties = new BlockProperties
        {
            StateProperties = [StateProperty.Enum("axis", "x", "z")],
            HasCollision = false,
            Opaque = false
        };
        var block = BlocksRegistry.Register("travelprobe:portal", blockProperties);
        Assert.True(BlocksRegistry.TryBind(block, blockProperties, owner, () => owner.IsClosing));
        var properties = new PortalProperties
        {
            IsFrame = candidate => candidate.RegistryId == BlockIds.DiamondBlock,
            FrameBlock = BlocksRegistry.DiamondBlock,
            PortalBlock = block.DefaultState,
            ResolveTarget = level => level == source.World ? target.World : source.World,
            CoordinateScale = (_, _) => 2,
            SearchRadius = _ => 16,
            Igniter = ItemsRegistry.Diamond,
            MinWidth = 3,
            MinHeight = 4,
            WaitTicks = 2
        };
        try
        {
            var type = PluginRegistry.RegisterPortal(owner, "travelprobe:gate", properties);
            var frame = Frame("x", 3, 4);
            foreach (var position in frame.Keys.ToArray())
                await source.World.SetBlockAsync(position, BlocksRegistry.DiamondBlock);

            Assert.False(await source.World.Portals.TryIgniteAsync(new Vector(0, 64, 0),
                igniter: new ItemStack(ItemsRegistry.Stick)));
            Assert.True(await source.World.Portals.TryIgniteAsync(new Vector(0, 64, 0),
                igniter: new ItemStack(ItemsRegistry.Diamond)));
            var lit = await PortalFrame.FindAsync(
                source.World.GetBlockAsync, new Vector(0, 64, 0), "x", -64, properties);
            Assert.True(lit!.Value.IsComplete);

            // A second type can share vanilla's portal block: its portals stay its own, beside the Nether's.
            var shared = PluginRegistry.RegisterPortal(owner, "travelprobe:shared", properties with
            {
                IsFrame = candidate => candidate.RegistryId == BlockIds.GoldBlock,
                FrameBlock = BlocksRegistry.GoldBlock,
                PortalBlock = BlocksRegistry.NetherPortal,
                Igniter = ItemsRegistry.Emerald
            });
            foreach (var position in Frame("x", 3, 4).Keys)
                await source.World.SetBlockAsync(position + new Vector(0, 0, -8), BlocksRegistry.GoldBlock);

            Assert.True(await source.World.Portals.TryIgniteAsync(new Vector(0, 64, -8),
                igniter: new ItemStack(ItemsRegistry.Emerald)));
            var sharedPortal = new Vector(0, 64, -8);
            var commandPortal = new Vector(-16, 70, -16);
            await source.World.SetBlockAsync(commandPortal, BlocksRegistry.NetherPortal);
            Assert.Same(shared, source.World.Portals.TypeAt(
                sharedPortal, (await source.World.GetBlockAsync(sharedPortal))!));
            Assert.Same(PortalTypesRegistry.Nether, source.World.Portals.TypeAt(
                commandPortal, (await source.World.GetBlockAsync(commandPortal))!));

            // Replacing a claimed portal twice before its tick must not give the new block its old claim.
            var replacedPortal = sharedPortal + Vector.Up;
            await source.World.SetBlockAsync(replacedPortal, BlocksRegistry.Air);
            await source.World.SetBlockAsync(replacedPortal, BlocksRegistry.NetherPortal);
            Assert.Same(PortalTypesRegistry.Nether, source.World.Portals.TypeAt(
                replacedPortal, (await source.World.GetBlockAsync(replacedPortal))!));

            var player = TestServerFactory.CreateConnectedPlayer(
                source.Server, source.World, "PortalTraveller", new VectorD(1.5, 64, 0.5));
            Assert.True(source.World.TryAddPlayer(player));
            Assert.True(source.World.TryAddEntity(player));
            await player.TickPortalsAsync();
            await player.TickPortalsAsync();
            Assert.Same(source.World, player.Level);
            for (var tick = 0; tick < 20 && player.Level == source.World; tick++)
            {
                await player.TickPortalsAsync();
                await Task.Delay(5);
            }

            Assert.Same(target.World, player.Level);
            Assert.Equal(10, player.PortalCooldown);
            var exitBlock = await target.World.GetBlockAsync((Vector)player.Position.Floor());
            Assert.Equal(block.Id, exitBlock!.RegistryId);
            var exit = await PortalFrame.FindAsync(
                target.World.GetBlockAsync, (Vector)player.Position.Floor(), "x", -64, properties);
            Assert.True(exit!.Value.IsComplete);
            Assert.Equal(3, exit.Value.Width);
            Assert.Equal(4, exit.Value.Height);
            // Empty-world fallback, with the scaled X/Z target and a frame-material safety platform.
            Assert.Equal(2, exit.Value.Origin.X);
            Assert.Equal(1, exit.Value.Origin.Z);
            Assert.Equal(70, exit.Value.Origin.Y);
            Assert.Equal(BlockIds.DiamondBlock,
                (await target.World.GetBlockAsync(exit.Value.Origin + Vector.Down))!.RegistryId);
            var teleport = ((TestClient)player.Client).SentPackets.OfType<PlayerPositionPacket>().Last();
            Assert.Equal(0x1E0, teleport.RelativeFlags);

            var mob = new Cow
            {
                Level = source.World,
                EntityId = 987655,
                Type = EntityType.Cow,
                Position = new VectorD(1.5, 64, 0.5)
            };
            Assert.True(source.World.TryAddEntity(mob));
            var chick = new Chicken
            {
                Level = source.World,
                EntityId = 987658,
                Type = EntityType.Chicken,
                Position = new VectorD(1.5, 65.4, 0.5)
            };
            Assert.True(source.World.TryAddEntity(chick));
            Assert.True(chick.Ride(mob));
            mob.LeashTo(player);
            for (var tick = 0; tick < 20 && mob.Level == source.World; tick++)
            {
                // Only the root vehicle uses the portal; its passenger comes along.
                await chick.TickEntityPortalAsync();
                await mob.TickEntityPortalAsync();
                await Task.Delay(5);
            }

            Assert.Same(target.World, mob.Level);
            Assert.Same(mob, target.World.GetRegionForLocation(mob.Position)!.Entities[mob.EntityId]);
            Assert.Same(target.World, chick.Level);
            Assert.Same(mob, chick.Vehicle);

            // The lead is held by its holder's UUID across levels until the holder is found again.
            Assert.Null(mob.LeashHolder);
            mob.TickLeash();
            Assert.Same(player, mob.LeashHolder);

            var pendingTraveller = TestServerFactory.CreateConnectedPlayer(
                source.Server, source.World, "PendingTraveller", new VectorD(1.5, 64, 0.5));
            Assert.True(source.World.TryAddPlayer(pendingTraveller));
            Assert.True(source.World.TryAddEntity(pendingTraveller));
            for (var tick = 0; tick <= properties.WaitTicks; tick++)
                await pendingTraveller.TickPortalsAsync();

            PortalTypesRegistry.ReleasePlugin(owner);
            Assert.Null(await PortalTravel.FindContactAsync(player));

            // Unloaded, the shared type's portal is inert rather than a Nether portal, frame checks included.
            Assert.Null(source.World.Portals.TypeAt(sharedPortal, (await source.World.GetBlockAsync(sharedPortal))!));
            await source.World.SetBlockAsync(new Vector(0, 63, -8), BlocksRegistry.Air, true);
            await source.World.Portals.TickAsync();
            Assert.Equal(BlockIds.NetherPortal, (await source.World.GetBlockAsync(sharedPortal))!.RegistryId);
            await target.World.SetBlockAsync(exit.Value.Origin + Vector.Down, BlocksRegistry.Air, true);
            await target.World.Portals.TickAsync();
            Assert.Equal(block.Id, (await target.World.GetBlockAsync(exit.Value.Origin))!.RegistryId);
            Assert.False(await source.World.TryLightPortalAsync(type, new Vector(0, 64, 0)));
            Assert.Same(type, PluginRegistry.RegisterPortal(owner, "travelprobe:gate", properties));
            await pendingTraveller.TickPortalsAsync();
            Assert.Same(source.World, pendingTraveller.Level);

            // Once reloaded, active frames respond to destruction again.
            await target.World.SetBlockAsync(exit.Value.Origin + Vector.Down, BlocksRegistry.Stone, true);
            await target.World.Portals.TickAsync();
            Assert.True((await target.World.GetBlockAsync(exit.Value.Origin))!.IsAir);
        }
        finally
        {
            PortalTypesRegistry.ReleasePlugin(owner);
            BlocksRegistry.ReleasePlugin(owner);
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task RegisteredVanillaPortalsKeepWaitScaleFrameAndLevelChangeRules()
    {
        await using var context = TestServerFactory.CreateServer($"nether-travel-{Guid.NewGuid():N}");
        var world = context.World;
        var nether = AddDimension(context, "minecraft:the_nether");
        AddChunks(world);
        AddChunks(nether);
        Assert.Same(nether, ((IWorld)world).GetLevel("minecraft:the_nether"));
        Assert.Null(((IWorld)world).GetLevel("missing:dimension"));
        var origin = new Vector(-8, 64, -8);
        foreach (var (position, block) in Frame("z", 2, 3))
            await world.SetBlockAsync(position + new Vector(-8, 0, -8), block);

        Assert.True(await world.Portals.TryIgniteAsync(origin));
        var player = TestServerFactory.CreateConnectedPlayer(
            context.Server, world, "NetherTraveller", new VectorD(-7.5, 64, -7.5));
        Assert.True(world.TryAddPlayer(player));
        Assert.True(world.TryAddEntity(player));
        for (var tick = 0; tick < 80; tick++)
            await player.TickPortalsAsync();

        Assert.Same(world, player.Level);
        for (var tick = 0; tick < 20 && player.Level == world; tick++)
        {
            await player.TickPortalsAsync();
            await Task.Delay(5);
        }

        Assert.Same(nether, player.Level);
        var exit = await PortalFrame.FindAsync(
            nether.GetBlockAsync, (Vector)player.Position.Floor(), "z", nether.MinY);
        Assert.True(exit!.Value.IsComplete);
        Assert.Equal(2, exit.Value.Width);
        Assert.Equal(3, exit.Value.Height);
        Assert.Equal(new Vector(-1, 70, -2), exit.Value.Origin);
        Assert.Equal(10, player.PortalCooldown);
        Assert.Equal(16, PortalFrame.NetherProperties.SearchRadius!(nether));
        Assert.Equal(128, PortalFrame.NetherProperties.SearchRadius(world));
        Assert.Same(world, PortalFrame.NetherProperties.ResolveTarget(nether));
        var packet = Assert.Single(((TestClient)player.Client).SentPackets.OfType<RespawnPacket>());
        Assert.Equal("minecraft:the_nether", packet.CommonPlayerSpawnInfo.DimensionName);
        Assert.Equal(10, packet.CommonPlayerSpawnInfo.PortalCooldown);
        var client = (TestClient)player.Client;
        var sharedSpawn = Assert.Single(client.SentPackets.OfType<SetDefaultSpawnPositionPacket>()).Position;
        Assert.Equal(world.DimensionName, sharedSpawn.Dimension);
        Assert.Equal((Vector)world.LevelData.SpawnPosition.Floor(), sharedSpawn.Pos);

        var end = AddDimension(context, "minecraft:the_end");
        for (var z = -1; z <= 0; z++)
            end.LoadRegionByChunk(6, z).SetChunk(new Chunk(6, z, end.MinY, end.Height, ChunkGenStage.full));
        await player.TeleportAsync(nether, new VectorD(8.5, 75, 8.5));
        for (var tick = 0; tick < 10; tick++)
            await player.TickPortalsAsync();

        await nether.SetBlockAsync(new Vector(8, 75, 8), BlocksRegistry.EndPortal);
        client.ClearPackets();
        for (var tick = 0; tick < 20 && player.Level == nether; tick++)
        {
            await player.TickPortalsAsync();
            await Task.Delay(5);
        }

        Assert.Same(end, player.Level);
        Assert.Equal(new VectorD(100.5, 49, 0.5), player.Position);
        Assert.Equal(BlockIds.Obsidian, (await end.GetBlockAsync(new Vector(100, 48, 0)))!.RegistryId);
        Assert.True((await end.GetBlockAsync(new Vector(100, 49, 0)))!.IsAir);
        Assert.Equal(10, Assert.Single(client.SentPackets.OfType<RespawnPacket>())
            .CommonPlayerSpawnInfo.PortalCooldown);
        var endPosition = Assert.Single(client.SentPackets.OfType<PlayerPositionPacket>());
        Assert.Equal(0, endPosition.RelativeFlags);
        Assert.Equal(VectorD.Zero, endPosition.Delta);
        Assert.Equal(world.DimensionName,
            Assert.Single(client.SentPackets.OfType<SetDefaultSpawnPositionPacket>()).Position.Dimension);

        // Items and other entities use End portals too, arriving at vanilla's End spawn point facing west.
        var item = new ItemEntity
        {
            Level = nether,
            EntityId = 987659,
            Position = new VectorD(8.5, 75.2, 8.5),
            Item = new ItemStack(ItemsRegistry.Diamond)
        };
        Assert.True(nether.TryAddEntity(item));
        for (var tick = 0; tick < 20 && item.Level == nether; tick++)
        {
            await item.TickEntityPortalAsync();
            await Task.Delay(5);
        }

        Assert.Same(end, item.Level);
        Assert.Equal(new VectorD(100.5, 50, 0.5), item.Position);
        Assert.Equal(90f, item.Yaw.Degrees);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task EndExitPortalAfterDragonDeathRollsCreditsAndClientCommandRespawnsAlivePlayer()
    {
        await using var context = TestServerFactory.CreateServer($"end-credits-{Guid.NewGuid():N}");
        var world = context.World;
        var end = AddDimension(context, "minecraft:the_end");
        for (var x = -8; x <= 8; x++)
            for (var z = -8; z <= 8; z++)
                end.LoadRegionByChunk(x, z).SetChunk(new Chunk(x, z, end.MinY, end.Height, ChunkGenStage.full));
        AddChunks(world);

        // Ground at (0, 63, 0) so the End fight initializes exitPortal at (0, 64, 0).
        await end.SetBlockAsync(new Vector(0, 63, 0), BlocksRegistry.EndStone);

        var player = TestServerFactory.CreateConnectedPlayer(
            context.Server, world, "DragonSlayer", new VectorD(10.5, 64, 10.5));
        Assert.True(world.TryAddPlayer(player));
        Assert.True(world.TryAddEntity(player));
        await player.TeleportAsync(end, new VectorD(10.5, 64, 10.5));
        var client = (TestClient)player.Client;

        // First End fight tick spawns the fight dragon and builds the inactive bedrock exit frame.
        await end.TickEndFightAsync();
        var dragon = Assert.Single(end.GetEntitiesInRange(new VectorD(0, 128, 0), 256).OfType<EnderDragon>());
        Assert.NotEqual(BlockIds.EndPortal, (await end.GetBlockAsync(new Vector(1, 64, 0)))!.RegistryId);

        // Killing the dragon lights the exit portal and places the dragon egg.
        await end.EndFightDragonKilledAsync(dragon);
        Assert.Equal(BlockIds.EndPortal, (await end.GetBlockAsync(new Vector(1, 64, 0)))!.RegistryId);
        Assert.Equal(BlockIds.DragonEgg, (await end.GetBlockAsync(new Vector(0, 68, 0)))!.RegistryId);

        // Stepping onto the exit portal's bedrock base at y = 64 contacts the EndPortal block at (1, 64, 0).
        await player.TeleportAsync(end, new VectorD(1.5, 64, 0.5));
        client.ClearPackets();

        // Like vanilla's, the End portal has no wait: the first portal tick in it rolls the credits on the first exit,
        // and travel pauses until the client responds.
        await player.TickPortalsAsync();
        Assert.True(player.AwaitingEndCredits);
        Assert.True(player.SeenCredits);
        Assert.Same(end, player.Level);
        var creditsPacket = Assert.Single(client.SentPackets.OfType<GameEventPacket>());
        Assert.Equal(ChangeGameStateReason.WinGame, creditsPacket.Reason);
        Assert.Equal(1f, creditsPacket.Value);

        // Neither a world tick nor more portal ticks bypass the credits and teleport the player early.
        await end.TickEndFightAsync();
        for (var tick = 0; tick < 10; tick++)
            await player.TickPortalsAsync();
        Assert.Same(end, player.Level);
        Assert.True(player.AwaitingEndCredits);
        Assert.Single(client.SentPackets.OfType<GameEventPacket>());

        // Alive player sending ClientCommandPacket(PerformRespawn) finishes the credits and returns to Overworld.
        Assert.True(player.Health > 0);
        await new Obsidian.Net.Packets.Play.Serverbound.ClientCommandPacket().HandleAsync(context.Server, player);
        Assert.False(player.AwaitingEndCredits);

        for (var tick = 0; tick < 20 && player.Level == end; tick++)
        {
            await player.TickPortalsAsync();
            await Task.Delay(5);
        }

        Assert.Same(world, player.Level);
        Assert.Equal(world.LevelData.SpawnPosition, player.Position);

        // Re-entering the End exit portal after having seen credits sends JustRespawnPlayer (0f), once the return's
        // portal cooldown ran out away from portals: like vanilla's, standing in a portal keeps restarting it.
        for (var tick = 0; tick < 10; tick++)
            await player.TickPortalsAsync();
        Assert.Equal(0, player.PortalCooldown);

        await player.TeleportAsync(end, new VectorD(1.5, 64, 0.5));
        client.ClearPackets();
        await player.TickPortalsAsync();
        Assert.True(player.AwaitingEndCredits);
        var skipCreditsPacket = Assert.Single(client.SentPackets.OfType<GameEventPacket>());
        Assert.Equal(ChangeGameStateReason.WinGame, skipCreditsPacket.Reason);
        Assert.Equal(0f, skipCreditsPacket.Value);
    }

    private static WorldData.Dimension AddDimension(RealServerContext context, string identifier)
    {
        var world = context.World;
        var dimension = new WorldData.Dimension(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorldData.Dimension>.Instance,
            context.Broadcaster,
            new TestOptionsMonitor<Obsidian.API.Configuration.ServerConfiguration>(world.Configuration),
            context.EventDispatcher,
            new Obsidian.WorldData.Generators.EmptyWorldGenerator(),
            identifier,
            world);
        dimension.Initialize(CodecRegistry.Dimensions.All[identifier]);
        world.RegisterDimension(dimension);
        return dimension;
    }

    private sealed class TravelCow : Cow
    {
        internal bool AddRider(Entities.Player player) => this.Mount(player);
    }

    private static void AddChunks(AbstractLevel world)
    {
        for (var x = -3; x <= 3; x++)
            for (var z = -3; z <= 3; z++)
                world.LoadRegionByChunk(x, z).SetChunk(new Chunk(x, z, world.MinY, world.Height, ChunkGenStage.full));
    }

    private static Dictionary<Vector, IBlock> Frame(string axis, int width, int height)
    {
        var blocks = new Dictionary<Vector, IBlock>();
        var step = axis == "x" ? new Vector(1, 0, 0) : new Vector(0, 0, 1);
        var origin = new Vector(0, 64, 0);
        for (var x = 0; x < width; x++)
        {
            blocks[origin + step * x + Vector.Down] = BlocksRegistry.ObsidianBlock;
            blocks[origin + step * x + Vector.Up * height] = BlocksRegistry.ObsidianBlock;
        }
        for (var y = 0; y < height; y++)
        {
            blocks[origin - step + Vector.Up * y] = BlocksRegistry.ObsidianBlock;
            blocks[origin + step * width + Vector.Up * y] = BlocksRegistry.ObsidianBlock;
        }
        return blocks;
    }
}
