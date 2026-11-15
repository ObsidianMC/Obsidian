#nullable enable
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Entities;
using Obsidian.Net;
using Obsidian.Net.ClientHandlers;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using Xunit;

namespace Obsidian.Tests;

public sealed class PickItems
{
    private static readonly Vector Target = new(8, 65, 8);
    private static readonly VectorD Start = new(8.5, 64, 8.5);

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PickPacketsDispatchAndDeliverHeldSlotBeforeInventoryChanges()
    {
        await using var context = TestServerFactory.CreateServer($"pick-dispatch-{Guid.NewGuid():N}");
        LoadChunk(context.World);
        await context.World.SetBlockAsync(Target, BlocksRegistry.Stone);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Picker", Start);
        await player.SetGamemodeAsync(GameMode.Creative);
        var receiver = (TestClient)player.Client;
        receiver.ClearPackets();

        using var transport = new Client(context.EventDispatcher, context.Server, NullLoggerFactory.Instance,
            context.UserCache, null!, context.Provider, null!);
        typeof(Client).GetProperty(nameof(Client.Player))!.SetValue(transport, player);
        var handler = new PlayClientHandler { Client = transport };
        await handler.HandleAsync(BlockPayload(Target, false));

        var packets = Delivered(receiver);
        Assert.Collection(packets,
            packet => Assert.Equal(0, Assert.IsType<SetHeldSlotPacket>(packet).Slot),
            packet => AssertSlot(packet, 36, ItemsRegistry.Stone, 1, 1));

        var pig = new Pig { Level = context.World, EntityId = Server.GetNextEntityId(), Position = Start };
        Assert.True(context.World.TryAddEntity(pig));
        var egg = ItemsRegistry.Get("minecraft:pig_spawn_egg");
        player.Inventory.SetItem(12, new ItemStack(egg, 7));
        await player.SetGamemodeAsync(GameMode.Survival);
        receiver.ClearPackets();
        await handler.HandleAsync(EntityPayload(pig.EntityId, true));

        Assert.Collection(Delivered(receiver),
            packet => Assert.Equal(1, Assert.IsType<SetHeldSlotPacket>(packet).Slot),
            packet => AssertSlot(packet, 12, null, 0, 2),
            packet => AssertSlot(packet, 37, egg, 7, 3));
        Assert.Equal(7, player.GetHeldItem()!.Count);
        Assert.Null(player.Inventory.GetItem(12));
        Assert.True(player.Inventory.GetItem(36)!.Is(ItemsRegistry.Stone));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PickInventorySwapsPreserveStacksAndCompareComponents()
    {
        await using var context = TestServerFactory.CreateServer($"pick-inventory-{Guid.NewGuid():N}");
        LoadChunk(context.World);
        await context.World.SetBlockAsync(Target, BlocksRegistry.Stone);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Picker", Start);
        await player.SetGamemodeAsync(GameMode.Survival);
        var client = (TestClient)player.Client;
        var named = new ItemStack(ItemsRegistry.Stone, 23,
            ComponentBuilder.CustomName with { Value = ChatMessage.Simple("Keep me") });
        player.Inventory.SetItem(36, named);
        player.Inventory.SetItem(12, new ItemStack(ItemsRegistry.Stone, 9));
        client.ClearPackets();

        await PickBlockAsync(context.Server, player, Target);

        Assert.Equal(37, player.CurrentHeldItemSlot);
        Assert.Equal(9, player.GetHeldItem()!.Count);
        Assert.Same(named, player.Inventory.GetItem(36));
        Assert.Collection(Delivered(client),
            packet => Assert.IsType<SetHeldSlotPacket>(packet),
            packet => AssertSlot(packet, 12, null, 0, 1),
            packet => AssertSlot(packet, 37, ItemsRegistry.Stone, 9, 2));

        // The hotbar wins over a second matching stack in the main inventory, without moving or merging either.
        player.Inventory.SetItem(11, new ItemStack(ItemsRegistry.Stone, 31));
        player.CurrentHeldItemSlot = 4;
        client.ClearPackets();
        await PickBlockAsync(context.Server, player, Target);
        Assert.Equal(1, Assert.IsType<SetHeldSlotPacket>(Assert.Single(Delivered(client))).Slot);
        Assert.Equal(31, player.Inventory.GetItem(11)!.Count);
        Assert.Equal(9, player.GetHeldItem()!.Count);

        // With every hotbar slot occupied, the selected slot swaps into the source slot atomically.
        for (var slot = 36; slot < 45; slot++)
            player.Inventory.SetItem(slot, new ItemStack(ItemsRegistry.Dirt, slot - 30));
        player.CurrentHeldItemSlot = 4;
        var displaced = player.Inventory.GetItem(40);
        client.ClearPackets();
        await PickBlockAsync(context.Server, player, Target);
        Assert.Same(displaced, player.Inventory.GetItem(11));
        Assert.Equal(31, player.GetHeldItem()!.Count);
        Assert.Collection(Delivered(client),
            packet => Assert.IsType<SetHeldSlotPacket>(packet),
            packet => AssertSlot(packet, 11, ItemsRegistry.Dirt, 10, 3),
            packet => AssertSlot(packet, 40, ItemsRegistry.Stone, 31, 4));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PickCreativePreservesDisplacedStacksAndSurvivalNeverCreatesItems()
    {
        await using var context = TestServerFactory.CreateServer($"pick-missing-{Guid.NewGuid():N}");
        LoadChunk(context.World);
        await context.World.SetBlockAsync(Target, BlocksRegistry.Stone);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Picker", Start);
        await player.SetGamemodeAsync(GameMode.Survival);
        var client = (TestClient)player.Client;
        client.ClearPackets();
        await PickBlockAsync(context.Server, player, Target);
        Assert.IsType<SetHeldSlotPacket>(Assert.Single(Delivered(client)));
        Assert.Null(player.GetHeldItem());

        await player.SetGamemodeAsync(GameMode.Creative);
        for (var slot = 36; slot < 45; slot++)
            player.Inventory.SetItem(slot, new ItemStack(ItemsRegistry.Dirt, 5));
        var displaced = player.Inventory.GetItem(36);
        client.ClearPackets();
        await PickBlockAsync(context.Server, player, Target);
        Assert.Same(displaced, player.Inventory.GetItem(9));
        Assert.True(player.GetHeldItem()!.Is(ItemsRegistry.Stone));
        Assert.Collection(Delivered(client),
            packet => Assert.IsType<SetHeldSlotPacket>(packet),
            packet => AssertSlot(packet, 9, ItemsRegistry.Dirt, 5, 1),
            packet => AssertSlot(packet, 36, ItemsRegistry.Stone, 1, 2));

        // Vanilla creative replaces the selected stack only when there is nowhere to preserve it.
        for (var slot = 9; slot < 45; slot++)
            player.Inventory.SetItem(slot, new ItemStack(ItemsRegistry.Dirt, 5));
        client.ClearPackets();
        await PickBlockAsync(context.Server, player, Target);
        Assert.Collection(Delivered(client),
            packet => Assert.IsType<SetHeldSlotPacket>(packet),
            packet => AssertSlot(packet, 36, ItemsRegistry.Stone, 1, 3));
        Assert.Equal(35, Enumerable.Range(9, 36).Count(slot => player.Inventory.GetItem(slot)!.Is(ItemsRegistry.Dirt)));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PickRejectsUnreachableUnloadedAndMissingTargetsButDeliversValidPicks()
    {
        await using var context = TestServerFactory.CreateServer($"pick-range-{Guid.NewGuid():N}");
        LoadChunk(context.World);
        await context.World.SetBlockAsync(Target, BlocksRegistry.Stone);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Picker", Start);
        await player.SetGamemodeAsync(GameMode.Creative);
        var client = (TestClient)player.Client;
        client.ClearPackets();

        // Exactly six blocks from the nearest face is rejected; just inside is accepted.
        player.Position = new VectorD(15, 64, 8.5);
        await PickBlockAsync(context.Server, player, Target);
        Assert.Empty(Delivered(client));
        player.Position = new VectorD(14.999, 64, 8.5);
        await PickBlockAsync(context.Server, player, Target);
        Assert.Contains(Delivered(client), packet => packet is ContainerSetSlotPacket);

        client.ClearPackets();
        await PickBlockAsync(context.Server, player, new Vector(15, 65, 8));
        Assert.Empty(Delivered(client));
        await PickBlockAsync(context.Server, player, Target);
        Assert.IsType<SetHeldSlotPacket>(Assert.Single(Delivered(client)));

        client.ClearPackets();
        player.Position = new VectorD(24.5, 64, 8.5);
        var queued = context.World.ChunksToGenCount;
        await PickBlockAsync(context.Server, player, new Vector(24, 65, 8));
        Assert.Empty(Delivered(client));
        Assert.Equal(queued, context.World.ChunksToGenCount);
        player.Position = Start;
        await PickBlockAsync(context.Server, player, Target);
        Assert.IsType<SetHeldSlotPacket>(Assert.Single(Delivered(client)));

        var stand = new ArmorStand { Level = context.World, EntityId = Server.GetNextEntityId(), Position = Start };
        Assert.True(context.World.TryAddEntity(stand));
        client.ClearPackets();
        await PickEntityAsync(context.Server, player, int.MaxValue);
        Assert.Empty(Delivered(client));
        await PickEntityAsync(context.Server, player, stand.EntityId);
        Assert.Contains(Delivered(client), packet => packet is ContainerSetSlotPacket);

        client.ClearPackets();
        player.Position = new VectorD(stand.CurrentBox.Max.X + 8, 64, 8.5);
        await PickEntityAsync(context.Server, player, stand.EntityId);
        Assert.Empty(Delivered(client));
        player.Position -= new VectorD(0.001, 0, 0);
        await PickEntityAsync(context.Server, player, stand.EntityId);
        Assert.IsType<SetHeldSlotPacket>(Assert.Single(Delivered(client)));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PickBlockDataCopiesComponentsWithoutTakingContentsAndRequiresCreative()
    {
        await using var context = TestServerFactory.CreateServer($"pick-data-{Guid.NewGuid():N}");
        LoadChunk(context.World);
        await context.World.SetBlockAsync(Target, BlocksRegistry.Chest);
        var chest = Assert.IsAssignableFrom<ContainerBlockEntity>(await context.World.GetBlockEntityAsync(Target));
        chest.Inventory.CustomName = ChatMessage.Simple("Stored chest");
        chest.Inventory.SetItem(2, new ItemStack(ItemsRegistry.Diamond, 13));
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Picker", Start);
        await player.SetGamemodeAsync(GameMode.Creative);
        var client = (TestClient)player.Client;
        client.ClearPackets();
        await PickBlockAsync(context.Server, player, Target, true);

        var picked = player.GetHeldItem()!;
        var contents = picked.GetComponent<SimpleDataComponent<System.Collections.Immutable.ImmutableArray<ItemStack>>>(
            DataComponentType.Container)!.Value;
        Assert.Equal("Stored chest", picked.CustomName!.Text);
        Assert.Equal(13, contents[2].Count);
        Assert.True(contents[2].Is(ItemsRegistry.Diamond));
        Assert.Equal(13, chest.Inventory.GetItem(2)!.Count);
        contents[2].Count = 1;
        Assert.Equal(13, chest.Inventory.GetItem(2)!.Count);
        Assert.Collection(Delivered(client),
            packet => Assert.IsType<SetHeldSlotPacket>(packet),
            packet => AssertSlot(packet, 36, ItemsRegistry.Chest, 1, 1));

        // Control-pick in survival matches the plain chest, never the component-bearing creative copy.
        player.Inventory.SetItem(39, new ItemStack(ItemsRegistry.Chest, 4));
        await player.SetGamemodeAsync(GameMode.Survival);
        client.ClearPackets();
        await PickBlockAsync(context.Server, player, Target, true);
        Assert.Equal(3, Assert.IsType<SetHeldSlotPacket>(Assert.Single(Delivered(client))).Slot);
        Assert.Equal(4, player.GetHeldItem()!.Count);
        Assert.Null(player.GetHeldItem()!.GetComponent<SimpleDataComponent<System.Collections.Immutable.ImmutableArray<ItemStack>>>(
            DataComponentType.Container));
        Assert.Equal(13, chest.Inventory.GetItem(2)!.Count);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PickEntityFrameCopiesItsStackAndEmptyFrameKeepsItsName()
    {
        await using var context = TestServerFactory.CreateServer($"pick-frame-{Guid.NewGuid():N}");
        LoadChunk(context.World);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Picker", Start);
        await player.SetGamemodeAsync(GameMode.Creative);
        var frame = new GlowItemFrame
        {
            Level = context.World,
            EntityId = Server.GetNextEntityId(),
            Position = Start,
            CustomName = ChatMessage.Simple("Frame"),
            Item = new ItemStack(ItemsRegistry.Diamond, 3,
                ComponentBuilder.CustomName with { Value = ChatMessage.Simple("Framed") })
        };
        Assert.True(context.World.TryAddEntity(frame));
        var client = (TestClient)player.Client;
        client.ClearPackets();
        await PickEntityAsync(context.Server, player, frame.EntityId);
        Assert.Equal(3, player.GetHeldItem()!.Count);
        Assert.Equal(frame.Item, player.GetHeldItem());
        Assert.NotSame(frame.Item, player.GetHeldItem());
        Assert.Collection(Delivered(client),
            packet => Assert.IsType<SetHeldSlotPacket>(packet),
            packet => AssertSlot(packet, 36, ItemsRegistry.Diamond, 3, 1));

        frame.Item = null;
        client.ClearPackets();
        await PickEntityAsync(context.Server, player, frame.EntityId);
        Assert.True(player.GetHeldItem()!.Is(ItemsRegistry.GlowItemFrame));
        Assert.Equal("Frame", player.GetHeldItem()!.CustomName!.Text);
        Assert.Collection(Delivered(client),
            packet => Assert.IsType<SetHeldSlotPacket>(packet),
            packet => AssertSlot(packet, 37, ItemsRegistry.GlowItemFrame, 1, 2));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PickBlockResultsMatchCapturedVanilla()
    {
        await using var context = TestServerFactory.CreateServer($"pick-capture-{Guid.NewGuid():N}");
        var chunk = LoadChunk(context.World);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Picker", Start);
        await player.SetGamemodeAsync(GameMode.Creative);
        var client = (TestClient)player.Client;
        using var stream = typeof(PickItems).Assembly.GetManifestResourceStream("Obsidian.Tests.Assets.pick_blocks.json")!;
        using var fixture = JsonDocument.Parse(stream);
        Assert.Equal("26.3", fixture.RootElement.GetProperty("version").GetString());
        var mismatches = new List<string>();
        var deliveries = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var block = row.GetProperty("block").GetString()!;
            var expected = row.GetProperty("item").GetString()!;
            chunk.SetBlock(Target.X, Target.Y, Target.Z, BlocksRegistry.Get(block));
            for (var slot = 9; slot < 45; slot++)
                player.Inventory.SetItem(slot, null);
            player.CurrentHeldItemSlot = 0;
            client.ClearPackets();

            await PickBlockAsync(context.Server, player, Target);

            var actual = player.GetHeldItem()?.Holder.UnlocalizedName ?? "minecraft:air";
            var packets = Delivered(client);
            var expectedPackets = expected == "minecraft:air" ? 0 : 2;
            if (actual != expected || packets.Length != expectedPackets)
                mismatches.Add($"{block}: expected {expected}/{expectedPackets} packets, got {actual}/{packets.Length}");
            deliveries += packets.Length;
        }

        // The same replay includes successful deliveries, so empty-result cases cannot pass through a dead handler.
        Assert.True(deliveries > 0);
        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PickInventoryPrefersUnenchantedSlotsAndIgnoresEquipment()
    {
        await using var context = TestServerFactory.CreateServer($"pick-enchanted-{Guid.NewGuid():N}");
        LoadChunk(context.World);
        await context.World.SetBlockAsync(Target, BlocksRegistry.Stone);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Picker", Start);
        await player.SetGamemodeAsync(GameMode.Survival);
        var client = (TestClient)player.Client;
        player.Inventory.SetItem(45, new ItemStack(ItemsRegistry.Stone, 6));
        player.Inventory.SetItem(5, new ItemStack(ItemsRegistry.Stone, 2));
        client.ClearPackets();
        await PickBlockAsync(context.Server, player, Target);
        Assert.IsType<SetHeldSlotPacket>(Assert.Single(Delivered(client)));
        Assert.Null(player.GetHeldItem());
        Assert.Equal(6, player.Inventory.GetItem(45)!.Count);
        Assert.Equal(2, player.Inventory.GetItem(5)!.Count);

        for (var slot = 36; slot < 45; slot++)
        {
            player.Inventory.SetItem(slot, new ItemStack(ItemsRegistry.Dirt, 4,
                ComponentBuilder.Enchantments with { Value = [new Enchantment { Id = 0, Level = 1 }] }));
        }
        player.CurrentHeldItemSlot = 7;
        player.Inventory.SetItem(38, new ItemStack(ItemsRegistry.Diamond, 5));
        player.Inventory.SetItem(12, new ItemStack(ItemsRegistry.Stone, 17));
        client.ClearPackets();
        await PickBlockAsync(context.Server, player, Target);
        Assert.Equal(38, player.CurrentHeldItemSlot);
        Assert.True(player.Inventory.GetItem(43)!.Enchantments.Count > 0);
        Assert.Collection(Delivered(client),
            packet => Assert.Equal(2, Assert.IsType<SetHeldSlotPacket>(packet).Slot),
            packet => AssertSlot(packet, 12, ItemsRegistry.Diamond, 5, 1),
            packet => AssertSlot(packet, 38, ItemsRegistry.Stone, 17, 2));
    }

    private static Chunk LoadChunk(World world)
    {
        var chunk = new Chunk(0, 0);
        chunk.SetChunkStatus(ChunkGenStage.full);
        world.LoadRegionByChunk(0, 0).SetChunk(chunk);
        return chunk;
    }

    private static PacketData BlockPayload(Vector position, bool includeData)
    {
        var buffer = new NetworkBuffer();
        buffer.WritePosition(position);
        buffer.WriteBoolean(includeData);
        buffer.Offset = 0;
        return new PacketData { Id = PickItemFromBlockPacket.PacketId, NetworkBuffer = buffer };
    }

    private static PacketData EntityPayload(int id, bool includeData)
    {
        var buffer = new NetworkBuffer();
        buffer.WriteVarInt(id);
        buffer.WriteBoolean(includeData);
        buffer.Offset = 0;
        return new PacketData { Id = PickItemFromEntityPacket.PacketId, NetworkBuffer = buffer };
    }

    private static ValueTask PickBlockAsync(Server server, Entities.Player player, Vector position, bool includeData = false)
    {
        var packet = new PickItemFromBlockPacket();
        packet.Populate(BlockPayload(position, includeData).NetworkBuffer);
        return packet.HandleAsync(server, player);
    }

    private static ValueTask PickEntityAsync(Server server, Entities.Player player, int id)
    {
        var packet = new PickItemFromEntityPacket();
        packet.Populate(EntityPayload(id, true).NetworkBuffer);
        Assert.Equal(id, packet.EntityId);
        Assert.True(packet.IncludeData);
        return packet.HandleAsync(server, player);
    }

    private static IClientboundPacket[] Delivered(TestClient client) => client.SentPackets
        .SelectMany(packet => packet is BundledPacket bundle ? bundle.Packets.Cast<IClientboundPacket>() : new[] { packet }).ToArray();

    private static void AssertSlot(IClientboundPacket packet, int slot, Item? item, int count, int state)
    {
        var update = Assert.IsType<ContainerSetSlotPacket>(packet);
        Assert.Equal(0, update.ContainerId);
        Assert.Equal(slot, update.Slot);
        Assert.Equal(state, update.StateId);
        Assert.Equal(item, update.SlotData?.Holder);
        Assert.Equal(count, update.SlotData?.Count ?? 0);
    }
}
