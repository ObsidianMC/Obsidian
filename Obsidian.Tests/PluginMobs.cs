using Obsidian.API;
using Obsidian.API.Entities;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Nbt;
using Obsidian.API.Loot;
using Obsidian.API.Networking;
using Obsidian.API.Plugins;
using Obsidian.API.Registries;
using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Payloads;
using Obsidian.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

// Plugin loot tables are process-wide, like the block drop tests' overrides.
[Collection(nameof(PluginTagFiles))]
public sealed class PluginMobs
{
    private static readonly EntityDataField<bool> Saddled = EntityDataFields.Boolean();

    [Fact]
    public async Task FlyingMountSyncsDataTakesRiderInputAndDropsItsLoot()
    {
        await using var context = TestServerFactory.CreateServer($"plugin-mob-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = new Chunk(0, 0);
        chunk.SetChunkStatus(ChunkGenStage.full);
        world.LoadRegionByChunk(0, 0).SetChunk(chunk);

        var owner = new PluginContainer
        {
            Info = new PluginInfo("pluginmobs"),
            Archive = PluginArchive.Open("pluginmobs.obby", PluginArchives.Zip(new Dictionary<string, byte[]>
            {
                [PluginMetadata.FileName] = PluginArchives.Metadata("pluginmobs", "1.0.0", "Mobs")
            }))
        };
        var behavior = new DrakeBehavior();
        var properties = new EntityProperties
        {
            Width = 0.9f,
            Height = 0.9f,
            Movement = EntityMovementMode.Flying,
            PassengerAttachments = [new VectorF(0, 1, 0)],
            DataFields = [Saddled],
            Behavior = behavior
        };
        var definition = EntityDefinitionsRegistry.Register("pluginmobs:drake", EntityKind.Mob);
        Assert.True(EntityDefinitionsRegistry.TryBind(definition, properties, owner, () => false));
        Assert.Equal(16, Saddled.Index);

        var drake = Assert.IsType<PluginMob>(PluginEntity.Create(definition, world));
        drake.EntityId = Server.GetNextEntityId();
        drake.Position = new VectorD(8.5, 70, 8.5);
        Assert.True(world.TryAddEntity(drake));
        drake.InitializeAi(false);

        // Its data field follows vanilla's mob fields, only for connections that agreed on the Obsidian channel.
        drake.SetData(Saddled, true);
        Assert.True(drake.GetData(Saddled));

        byte[] Metadata(bool modded)
        {
            using var buffer = new NetworkBuffer { WritesPluginEntityData = modded };
            buffer.WriteEntity(drake);
            return buffer.AsSpan(0, buffer.Size).ToArray();
        }

        var vanillaData = Metadata(false);
        Assert.Equal([.. vanillaData[..^1], 16, (byte)EntityMetadataType.Boolean, 1, 0xff], Metadata(true));

        // API modifiers persist in vanilla's attributes format; metadata is transient like SynchedEntityData.
        Assert.True(drake.SetAttributeModifier(
            "minecraft:max_health", "pluginmobs:health", 5, AttributeOperation.Add));
        Assert.True(drake.TryAddAttribute("minecraft:attack_damage", 6));
        Assert.True(drake.SetAttributeModifier(
            "minecraft:attack_damage", "pluginmobs:attack", 2, AttributeOperation.Add));
        drake.SetModifier("minecraft:movement_speed", new("pluginmobs:temporary", 1, AttributeOperation.Add));
        var saved = EntityNbt.Save(drake)!;
        var restored = Assert.IsType<PluginMob>(EntityNbt.Load(saved, world));
        Assert.Equal(25, restored.GetAttributeValue("minecraft:max_health"));
        Assert.True(restored.HasAttribute("minecraft:attack_damage"));
        Assert.Equal(8, restored.GetAttributeValue("minecraft:attack_damage"));
        Assert.Equal(0.25f, restored.GetAttributeValue("minecraft:movement_speed"));
        Assert.False(restored.GetData(Saddled));
        Assert.True(saved.TryGetTag<NbtList>("attributes", out var attributes));
        var health = attributes.OfType<NbtCompound>()
            .Single(entry => entry.GetString("id") == "minecraft:max_health");
        Assert.True(health.TryGetTag<NbtList>("modifiers", out var modifiers));
        var modifier = Assert.Single(modifiers.OfType<NbtCompound>());
        Assert.Equal("add_value", modifier.GetString("operation"));
        Assert.True(restored.RemoveAttributeModifier("minecraft:max_health", "pluginmobs:health"));
        Assert.Equal(20, EntityNbt.Load(EntityNbt.Save(restored)!, world)!.GetAttributeValue("minecraft:max_health"));
        drake.RemoveAttributeModifier("minecraft:max_health", "pluginmobs:health");
        drake.RemoveAttributeModifier("minecraft:movement_speed", "pluginmobs:temporary");

        Assert.True(restored.SetAttributeModifier("minecraft:scale", "pluginmobs:size", 1, AttributeOperation.Add));
        Assert.Equal(1.8f, restored.Dimension.Width);
        Assert.Equal(1.8f, restored.Dimension.Height);
        Assert.Equal(1.53f, restored.EyeHeight, 5);
        Assert.Equal(1.8, restored.BoundingBox.Max.Y - restored.BoundingBox.Min.Y, 5);
        Assert.True(restored.RemoveAttributeModifier("minecraft:scale", "pluginmobs:size"));
        Assert.Equal(0.9f, restored.Dimension.Width);

        var player = TestServerFactory.CreateConnectedPlayer(
            context.Server,
            world,
            "Rider",
            new VectorD(8.5, 70, 10.5));
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);
        var client = (TestClient)player.Client;
        client.SetState(ClientState.Configuration);
        var channels = new ClientChannels(client, new PayloadChannelRegistry());
        client.Channels = channels;
        using (var query = new NetworkBuffer())
        {
            ChannelPayloads.WriteQuery(query, new Dictionary<NetworkProtocol, List<NetworkComponent>>
            {
                [NetworkProtocol.Play] = [new(ChannelPayloads.PluginEntityData, "1", PacketFlow.Clientbound, true)]
            });
            await channels.HandlePayloadAsync(ChannelPayloads.NeoForgeQuery, query.AsSpan(0, query.Size).ToArray());
        }

        Assert.True(await channels.FinishHandshakeAsync(0));
        client.SetState(ClientState.Play);
        Assert.True(channels.HasNeoForgePlayChannel(ChannelPayloads.PluginEntityData));

        // SpawnEggItem uses empty collision rather than air, and settles on a partial clicked block's top.
        var clicked = new Vector(4, 64, 4);
        var oldPosition = player.Position;
        player.Position = (VectorD)clicked + new VectorD(0, 1, 2);
        await world.SetBlockAsync(clicked + Vector.Down, BlocksRegistry.Stone);
        foreach (var (block, height) in new (IBlock Block, double Height)[]
        {
            (BlocksRegistry.ShortGrass, 64),
            (BlocksRegistry.OakSlab, 64.5)
        })
        {
            await world.SetBlockAsync(clicked, block);
            var egg = new ItemStack(ItemsRegistry.PigSpawnEgg, 1, ComponentBuilder.EntityData with
            {
                RegistryId = definition.Id
            });
            player.Inventory.SetItem(player.CurrentHeldItemSlot, egg);
            await BlockUpdates.UseOn(clicked, BlockFace.Up).HandleAsync(context.Server, player);
            var spawned = Assert.Single(world.GetEntitiesInRange((VectorD)clicked, 2).OfType<PluginMob>());
            Assert.Equal(new VectorD(4.5, height, 4.5), spawned.Position);
            Assert.True(await world.DestroyEntityAsync(spawned));
        }

        player.Position = oldPosition;

        // Using it mounts the player, whose input then flies it: ahead where the player looks, and up with jump.
        Assert.True(await drake.InteractAsync(player, InteractionHand.MainHand));
        Assert.Same(drake, ((IPlayer)player).Vehicle);

        player.Yaw = 0;
        player.Pitch = 0;
        player.Input = PlayerInput.Forward | PlayerInput.Jump;
        var start = drake.Position;
        for (var tick = 0; tick < 10; tick++)
            await drake.TickAsync();

        Assert.True(drake.Position.Z > start.Z + 0.1);
        Assert.True(drake.Position.Y > start.Y + 0.05);
        Assert.True(player.Position.Y > drake.Position.Y);
        drake.SetAttributeModifier("minecraft:scale", "pluginmobs:size", 1, AttributeOperation.Add);
        await drake.TickAsync();
        Assert.Equal(1.4, player.Position.Y - drake.Position.Y, 5);
        drake.RemoveAttributeModifier("minecraft:scale", "pluginmobs:size");
        Assert.Contains(client.SentPackets, packet => packet is SetEntityDataPacket data && data.EntityId == drake.EntityId);

        // Once the rider's client sends where it moved the mob, the client moves it: the server takes a move it can
        // make, and stops steering from the input.
        var moved = drake.Position + new VectorD(0, 0.2, 0.4);
        drake.ClientControl.Enqueue(player, new VehicleMove(moved, 90, 0, false));
        await drake.TickAsync();
        Assert.Equal(moved, drake.Position);
        Assert.Equal(90, drake.Yaw.Degrees, 1);

        await drake.TickAsync();
        Assert.Equal(moved, drake.Position);

        // It refuses one far beyond the mob's motion, as vanilla does, and puts the client's mob back.
        client.ClearPackets();
        drake.ClientControl.Enqueue(player, new VehicleMove(moved + new VectorD(0, 0, 20), 90, 0, false));
        await drake.TickAsync();
        Assert.Equal(moved, drake.Position);
        Assert.Contains(
            client.SentPackets,
            packet => packet is MoveVehiclePacket { Position: var back } && back == moved);

        // Walking mounts still count floating when every packet claims to be on the ground in empty air.
        var walkingControl = new ClientVehicleControl();
        drake.NoGravity = false;
        for (var tick = 0; tick < 80; tick++)
        {
            walkingControl.Enqueue(player, new VehicleMove(moved, 90, 0, true));
            walkingControl.Tick(drake, player, flying: false);
            drake.MovementFlags = MovementFlags.OnGround;
        }

        Assert.False(walkingControl.FloatingTooLong);
        for (var tick = 80; tick < 100; tick++)
        {
            walkingControl.Enqueue(player, new VehicleMove(moved, 90, 0, true));
            walkingControl.Tick(drake, player, flying: false);
        }

        Assert.True(walkingControl.FloatingTooLong);
        drake.NoGravity = true;

        player.Input = PlayerInput.Sneak;
        await drake.TickAsync();
        Assert.Null(((IPlayer)player).Vehicle);

        // Its hurt hook cancels fall damage and lowers small hits; it dies to a big one, telling its death hook, and
        // drops its loot table with the player's looting.
        await drake.DamageCombatAsync(drake, 5, CombatDamageKind.Fall);
        Assert.Equal(20, drake.Health);

        await drake.DamageCombatAsync(player, 10, CombatDamageKind.Melee);
        Assert.Equal(19, drake.Health);

        PluginLootTables.Apply([LootTableJson.Parse("""
            { "type": "minecraft:entity", "pools": [{
              "rolls": 1,
              "conditions": [{ "condition": "minecraft:killed_by_player" }],
              "entries": [{ "type": "minecraft:item", "name": "minecraft:diamond", "functions": [
                { "function": "minecraft:enchanted_count_increase", "enchantment": "minecraft:looting", "count": 1 }
              ] }]
            }] }
            """, "pluginmobs:entities/drake")]);
        try
        {
            var sword = ItemsRegistry.GetSingleItem(ItemsRegistry.IronSword);
            EnchantmentHelper.Enchant(sword, EnchantmentsRegistry.Looting, 2);
            player.Inventory.SetItem(player.CurrentHeldItemSlot, sword);
            // A projectile remembers a different weapon; looting reads what its attacker holds at death.
            var damageWeapon = ItemsRegistry.GetSingleItem(ItemsRegistry.Bow);
            await drake.DamageCombatAsync(player, 100, CombatDamageKind.Melee, damageWeapon);
        }
        finally
        {
            PluginLootTables.Apply([]);
        }

        Assert.False(drake.Alive);
        Assert.Equal(["minecraft:fall", "minecraft:player_attack", "minecraft:player_attack"], behavior.Hurt);
        Assert.Equal("minecraft:player_attack", behavior.Death?.Type);
        Assert.Same(player, behavior.Death?.Attacker);

        var drop = Assert.Single(world.GetEntitiesInRange(drake.Position, 3).OfType<ItemEntity>());
        Assert.Equal(ItemsRegistry.Diamond, drop.Item.Holder);
        Assert.Equal(3, drop.Item.Count);
    }

    private sealed class DrakeBehavior : IEntityBehavior
    {
        public List<string> Hurt { get; } = [];

        public DamageSource? Death { get; private set; }

        public bool CanMount(IEntity entity, IPlayer player) => true;

        public ValueTask HurtAsync(IEntity entity, EntityDamage damage)
        {
            this.Hurt.Add(damage.Source.Type);
            damage.Cancelled = damage.Source.Type == "minecraft:fall";

            if (damage.Amount < 50)
                damage.Amount = 1;

            return ValueTask.CompletedTask;
        }

        public ValueTask DiedAsync(IEntity entity, DamageSource source)
        {
            this.Death = source;
            return ValueTask.CompletedTask;
        }
    }
}
