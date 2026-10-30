using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.ChunkData;
using Obsidian.Registries;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Smoke tests of cooking: furnaces, blast furnaces and smokers cook with their fuel's burn time and speed (26.1),
/// across a save, fed and emptied by hoppers, awarding experience; and campfires cook while keeping their items'
/// private components on disk.
/// </summary>
[Collection(nameof(PluginTagFiles))]
public sealed class Cooking
{
    private static readonly Vector position = new(2, 70, 2);

    // Each machine cooks its inputs with one coal. 26.1 recipes give every machine 200 ticks; a blast furnace or
    // smoker burns coal for half as long (800 ticks) and cooks twice as fast. The smoker cooks three beef so that
    // their experience (0.35 each) adds up to at least one orb.
    private static readonly Dictionary<string, Machine> machines = new()
    {
        ["furnace"] = new(BlocksRegistry.Furnace, ItemsRegistry.RawIron, ItemsRegistry.IronIngot,
            Count: 2, CookTime: 200, BurnTime: 1600, Speed: 1f),
        ["blast furnace"] = new(BlocksRegistry.BlastFurnace, ItemsRegistry.RawIron, ItemsRegistry.IronIngot,
            Count: 2, CookTime: 100, BurnTime: 800, Speed: 2f),
        ["smoker"] = new(BlocksRegistry.Smoker, ItemsRegistry.Beef, ItemsRegistry.CookedBeef,
            Count: 3, CookTime: 100, BurnTime: 800, Speed: 2f)
    };

    // Each row burns a stack given minecraft:cooking_fuel while a furnace cooks raw iron. Like vanilla's furnace, the
    // fuel item's crafting remainder takes the fuel slot once the stack is used up, and drops otherwise.
    private static readonly Dictionary<string, FuelRemainder> fuelRemainders = new()
    {
        ["A milk bucket leaves its bucket in the fuel slot"] = new(
            ItemsRegistry.MilkBucket,
            Count: 1,
            InSlot: ItemsRegistry.Bucket,
            Dropped: null),
        ["A honey bottle drops its glass bottle while more are left"] = new(
            ItemsRegistry.HoneyBottle,
            Count: 2,
            InSlot: ItemsRegistry.HoneyBottle,
            Dropped: ItemsRegistry.GlassBottle)
    };

    public static TheoryData<string> Machines => [.. machines.Keys];

    public static TheoryData<string> FuelRemainderRows => [.. fuelRemainders.Keys];

    [Theory(DisplayName = "Cooking machines cook across a save and automation and award experience",
        Timeout = TestServerFactory.TestTimeout)]
    [MemberData(nameof(Machines))]
    public async Task MachinesCookAcrossSaveAndAutomationAndAwardExperience(string name)
    {
        var machine = machines[name];
        await using var kitchen = await Kitchen.OpenAsync(name);
        var half = machine.CookTime / 2;

        // A hopper above feeds the input; one from the side feeds the coal.
        var furnace = await kitchen.PlaceAsync<FurnaceBlockEntity>(machine.Block);
        Assert.Null(kitchen.Insert(BlockFace.Up, new ItemStack(machine.Input, machine.Count)));
        Assert.Null(kitchen.Insert(BlockFace.East, new ItemStack(ItemsRegistry.Coal, 1)));

        // Halfway through the first item, the coal lit the machine at its speed.
        await kitchen.TickAsync(furnace, half);

        Assert.Equal(half, furnace.CookingSpent);
        Assert.Equal(machine.BurnTime - half + 1, furnace.LitRemaining);
        Assert.Equal("true", kitchen.Block.GetProperty("lit"));

        // The save keeps the progress and the speed.
        var saved = BlockEntityNbt.SaveData(furnace);

        Assert.Equal(half, saved.GetInt("cooking_time_spent"));
        Assert.Equal(machine.CookTime, saved.GetInt("cooking_total_time"));
        Assert.Equal(machine.BurnTime, saved.GetInt("lit_total_time"));
        Assert.Equal(machine.Speed, saved.GetFloat("speed_multiplier"));

        // Reloaded, it finishes the first item on time.
        var restored = kitchen.Restore<FurnaceBlockEntity>(furnace.Id, saved);
        await kitchen.TickAsync(restored, machine.CookTime - half);

        Assert.True(restored.Inventory[2]?.Is(machine.Output));
        Assert.Equal(machine.Count - 1, restored.Inventory[0]?.Count);
        Assert.Equal(1, restored.RecipesUsed.Values.Sum());

        // A hopper below takes the result without awarding its experience.
        Assert.True(kitchen.Extract(BlockFace.Down)?.Is(machine.Output));
        Assert.Equal(1, restored.RecipesUsed.Values.Sum());

        // A player taking the rest gets the menu's four values and every cooked item's experience.
        await kitchen.TickAsync(restored, (machine.Count - 1) * machine.CookTime);
        await kitchen.OpenAsync(restored.Furnace);

        Assert.Equal(4, kitchen.ContainerDataPackets());

        await kitchen.TakeResultAsync();

        Assert.True(kitchen.Player.CarriedItem?.Is(machine.Output));
        Assert.Empty(restored.RecipesUsed);
        Assert.Contains(kitchen.Entities(), entity => entity is ExperienceOrb);
    }

    [Fact(DisplayName = "A furnace burning lava dries a wet sponge into the empty bucket",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task WetSpongeFillsTheLavaBucket()
    {
        await using var kitchen = await Kitchen.OpenAsync("sponge");
        var furnace = await kitchen.PlaceAsync<FurnaceBlockEntity>(BlocksRegistry.Furnace);
        furnace.Inventory[0] = new ItemStack(ItemsRegistry.WetSponge, 1);
        furnace.Inventory[1] = new ItemStack(ItemsRegistry.LavaBucket, 1);

        await kitchen.TickAsync(furnace, 200);

        Assert.Equal(20000, furnace.LitTotal);
        Assert.True(furnace.Inventory[1]?.Is(ItemsRegistry.WaterBucket));
        Assert.True(furnace.Inventory[2]?.Is(ItemsRegistry.Sponge));
    }

    [Theory(DisplayName = "A furnace keeps what its fuel leaves behind, like vanilla",
        Timeout = TestServerFactory.TestTimeout)]
    [MemberData(nameof(FuelRemainderRows))]
    public async Task FuelLeavesItsCraftingRemainder(string row)
    {
        var fuel = fuelRemainders[row];
        await using var kitchen = await Kitchen.OpenAsync("fuel remainder");
        var furnace = await kitchen.PlaceAsync<FurnaceBlockEntity>(BlocksRegistry.Furnace);
        furnace.Inventory[0] = new ItemStack(ItemsRegistry.RawIron, 1);
        furnace.Inventory[1] = CookingFuel(fuel.Item, fuel.Count, burnTime: 1600);

        await kitchen.TickAsync(furnace, 1);

        Assert.Equal(1600, furnace.LitTotal);
        Assert.True(furnace.Inventory[1]?.Is(fuel.InSlot));
        Assert.Equal(1, furnace.Inventory[1]?.Count);

        var dropped = kitchen.Entities()
            .OfType<ItemEntity>()
            .Select(entity => entity.Item.Holder)
            .ToArray();
        if (fuel.Dropped is Item item)
            Assert.Equal([item], dropped);
        else
            Assert.Empty(dropped);
    }

    [Fact(DisplayName = "A campfire cooks across a save, showing items without their private components",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task CampfireCooksAcrossSaveAndHidesPrivateComponents()
    {
        await using var kitchen = await Kitchen.OpenAsync("campfire");
        await kitchen.World.SetBlockAsync(position, BlocksRegistry.Campfire);

        // A player puts one of two beef with a plugin's private component on the campfire.
        var privateType = DataComponentTypesRegistry.Register("cooking:private");
        var component = new PluginDataComponent(privateType.Type, privateType.Identifier, new NbtTag<int>("", 7));
        kitchen.Player.Inventory[36] = new ItemStack(ItemsRegistry.Beef, 2, component);

        Assert.True(await kitchen.World.UseCampfireAsync(kitchen.Player, position, 36));
        Assert.Equal(1, kitchen.Player.Inventory[36]?.Count);

        // Halfway through, the save keeps the component and the client's view leaves it out.
        var campfire = Assert.IsType<CampfireBlockEntity>(kitchen.World.GetResidentBlockEntity(position));
        await kitchen.TickAsync(campfire, 100);
        var saved = BlockEntityNbt.SaveData(campfire);

        Assert.Equal(100, FirstCookingTime(saved));

        var savedInput = FirstItem(saved);
        var visibleInput = FirstItem(campfire.GetUpdateData()!);

        Assert.Equal(7, Assert.IsType<NbtCompound>(savedInput["components"]).GetInt(privateType.Identifier));
        Assert.False(visibleInput.HasTag("components"));

        // Data-backed vanilla displays use the same projection, while keeping their full item data on disk.
        var pot = DecoratedPotHolding(savedInput);

        Assert.False(Assert.IsType<NbtCompound>(pot.GetUpdateData()!["item"]).HasTag("components"));
        Assert.True(Assert.IsType<NbtCompound>(pot.SnapshotData()["item"]).HasTag("components"));

        // Reloaded and put out, it cools by two ticks a tick.
        var reloaded = kitchen.Restore<CampfireBlockEntity>("minecraft:campfire", saved);
        await kitchen.World.SetBlockAsync(position, BlocksRegistry.Campfire.WithProperty("lit", false));
        await kitchen.TickAsync(reloaded, 1);

        Assert.Equal(98, FirstCookingTime(BlockEntityNbt.SaveData(reloaded)));

        // Lit again, it finishes and drops the cooked beef.
        await kitchen.World.SetBlockAsync(position, BlocksRegistry.Campfire);
        await kitchen.TickAsync(reloaded, 502);

        Assert.Null(reloaded.Inventory[0]);
        Assert.Contains(
            kitchen.Entities(),
            entity => entity is ItemEntity item && item.Item.Is(ItemsRegistry.CookedBeef));
        Assert.Null(kitchen.World.GetItemAccess(position, BlockFace.Up));
    }

    // A stack given its own minecraft:cooking_fuel, burning for the given time at normal speed.
    private static ItemStack CookingFuel(Item item, int count, int burnTime)
    {
        var fuel = new FuelComponentData(new ComponentIntValue(burnTime), new ComponentFloatValue(1f));

        return new ItemStack(item, count, ComponentBuilder.CookingFuel with { Value = fuel });
    }

    private static int FirstCookingTime(NbtCompound tag)
    {
        Assert.True(tag.TryGetTag<NbtArray<int>>("CookingTimes", out var times));

        return times[0];
    }

    private static NbtCompound FirstItem(NbtCompound tag)
    {
        var items = Assert.IsType<NbtList>(tag["Items"]);

        return Assert.IsType<NbtCompound>(items[0]);
    }

    private static DataBlockEntity DecoratedPotHolding(NbtCompound item)
    {
        var pot = new DataBlockEntity
        {
            Id = "minecraft:decorated_pot"
        };

        var potItem = DataBlockEntity.Copy(item);
        potItem.Name = "item";
        potItem.Remove("count");
        pot.Set(potItem);

        return pot;
    }

    /// <summary>
    /// A cooking machine of a test row: the input it cooks into the output and how many, how long each takes, and
    /// how long coal burns in it and how fast it cooks.
    /// </summary>
    private sealed record Machine(
        IBlock Block,
        Item Input,
        Item Output,
        int Count,
        int CookTime,
        int BurnTime,
        float Speed);

    /// <summary>
    /// A fuel of a test row: the item and how many, and what's left in the fuel slot and dropped after one burns.
    /// </summary>
    private sealed record FuelRemainder(Item Item, int Count, Item InSlot, Item? Dropped);

    /// <summary>
    /// A server with an empty chunk and a joined player, cooking at <see cref="position"/>.
    /// </summary>
    private sealed class Kitchen : IAsyncDisposable
    {
        private readonly RealServerContext context;
        private readonly Chunk chunk;

        private Kitchen(RealServerContext context, Chunk chunk, Obsidian.Entities.Player player)
        {
            this.context = context;
            this.chunk = chunk;
            this.Player = player;
        }

        public World World => this.context.World;

        public Obsidian.Entities.Player Player { get; }

        public IBlock Block => this.World.BlockUpdates.GetBlock(position);

        public static async Task<Kitchen> OpenAsync(string name)
        {
            var context = TestServerFactory.CreateServer($"cooking-{name.Replace(' ', '-')}-{Guid.NewGuid():N}");
            context.World.RecipeSource = () => context.Server.RecipeRegistry.Current;
            var chunk = WorldBlockEntities.AddEmptyChunk(context.World);

            var player = TestServerFactory.CreateConnectedPlayer(
                context.Server,
                context.World,
                "Cook",
                (VectorD)position);
            await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);

            return new Kitchen(context, chunk, player);
        }

        public async Task<T> PlaceAsync<T>(IBlock block) where T : class
        {
            await this.World.SetBlockAsync(position, block);

            return Assert.IsType<T>(this.World.GetResidentBlockEntity(position));
        }

        // Replaces the block entity with one loaded from its save.
        public T Restore<T>(string id, NbtCompound saved) where T : BlockEntity
        {
            var restored = Assert.IsType<T>(BlockEntityNbt.Create(id, position, saved));
            this.chunk.SetBlockEntity(position.X, position.Y, position.Z, restored);
            this.World.AttachBlockEntity(restored);

            return restored;
        }

        public ItemStack? Insert(BlockFace side, ItemStack stack) =>
            this.World.GetItemAccess(position, side)!.Insert(stack);

        public ItemStack? Extract(BlockFace side) => this.World.GetItemAccess(position, side)!.Extract(0, 1);

        public async Task TickAsync(FurnaceBlockEntity furnace, int ticks)
        {
            for (var tick = 0; tick < ticks; tick++)
                await this.World.TickFurnaceAsync(furnace);
        }

        public async Task TickAsync(CampfireBlockEntity campfire, int ticks)
        {
            for (var tick = 0; tick < ticks; tick++)
                await this.World.TickCampfireAsync(campfire);
        }

        public ValueTask OpenAsync(BaseContainer container) => this.Player.OpenInventoryAsync(container);

        // The menu data packets the player's open container got.
        public int ContainerDataPackets()
        {
            var client = (TestClient)this.Player.Client;

            return client.SentPackets
                .OfType<ContainerSetDataPacket>()
                .Count(packet => packet.ContainerId == this.Player.CurrentContainerId);
        }

        // Clicks the open furnace's result slot, as the client sends it.
        public async Task TakeResultAsync()
        {
            using var writer = new NetworkBuffer();
            writer.WriteVarInt(this.Player.CurrentContainerId);
            writer.WriteVarInt(0);
            writer.WriteShort(2);
            writer.WriteByte(0);
            writer.WriteVarInt((int)ClickType.Pickup);
            writer.WriteVarInt(0);
            writer.WriteBoolean(false);

            using var reader = new NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());
            var packet = new ContainerClickPacket();
            packet.Populate(reader);
            await packet.HandleAsync(this.Player.Server, this.Player);
        }

        public IEnumerable<IEntity> Entities() =>
            this.World.Regions.Values.SelectMany(region => region.Entities.Values);

        public async ValueTask DisposeAsync()
        {
            this.Player.CarriedItem = null;
            await this.context.DisposeAsync();
        }
    }
}
