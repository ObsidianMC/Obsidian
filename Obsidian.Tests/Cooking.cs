using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.Loot.Conditions;
using Obsidian.API.Loot.Numbers;
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
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Smoke tests of cooking: furnaces, blast furnaces and smokers cook with their fuel's burn time and speed (26.1),
/// across a save, fed and emptied by hoppers, awarding experience and the recipe; and campfires cook while keeping
/// their items' private components on disk.
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

    [Fact(DisplayName = "Cooking machines cook across a save and automation and award experience",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task MachinesCookAcrossSaveAndAutomationAndAwardExperience()
    {
        await FixtureCases.AssertAllAsync(machines.Keys, name => name, AssertMachineCooksAcrossSaveAndAutomation);
    }

    private static async Task AssertMachineCooksAcrossSaveAndAutomation(string name)
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

        // A player taking the rest gets the menu's four values, every cooked item's experience and the recipe, which
        // meets its recipe advancement's recipe_unlocked criterion.
        await kitchen.TickAsync(restored, (machine.Count - 1) * machine.CookTime);
        await kitchen.OpenAsync(restored.Furnace);

        Assert.Equal(4, kitchen.ContainerDataPackets());

        await kitchen.TakeResultAsync();

        // The recipe comes on the player's next tick.
        await kitchen.Player.TickAsync();

        Assert.True(kitchen.Player.CarriedItem?.Is(machine.Output));
        Assert.Empty(restored.RecipesUsed);
        Assert.Contains(kitchen.Entities(), entity => entity is ExperienceOrb);
        Assert.Contains(AdvancementsRegistry.ForTrigger("minecraft:recipe_unlocked"),
            advancement => kitchen.Player.Advancements.HasCriterion(advancement.Identifier, "has_the_recipe"));
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

        // A decorated pot shows its item the same way, while keeping its full item data on disk.
        var pot = DecoratedPotHolding(savedInput);

        Assert.False(Assert.IsType<NbtCompound>(pot.GetUpdateData()!["item"]).HasTag("components"));
        Assert.True(Assert.IsType<NbtCompound>(BlockEntityNbt.SaveData(pot)["item"]).HasTag("components"));

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

    // Taking a result takes the machine's recipes with the output; the player is given them on their next tick or as
    // they leave, before the leave's save. A player who took a result and left before their next tick comes back with
    // the recipe and its criterion. A player who left first takes nothing from the machine, which keeps the recipe.
    // A crafted recipe's recipe_crafted fires on the leave too.
    [Fact(DisplayName = "Taking a result and leaving before the next tick keeps the recipe whole",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task TakingAResultAndLeavingKeepsTheRecipeWhole()
    {
        await using var awards = RecipeAwards.Apply(RecipeAwards.Crafted(CraftingTableRecipe));
        await using var kitchen = await Kitchen.OpenAsync("taking and leaving");
        var furnace = await kitchen.PlaceAsync<FurnaceBlockEntity>(BlocksRegistry.Furnace);

        // Taken, then left before the next tick. The player's data is loaded, so that their saves are written.
        var cook = kitchen.Player;
        await cook.LoadAsync(loadFromPersistentWorld: false);
        await kitchen.TakeCookedAsync(furnace, cook, IronIngotRecipe);

        Assert.Equal((false, false), Seen(cook, IronIngotRecipe));
        Assert.Equal(0, furnace.RecipesUsed.Values.Sum());

        await LeaveAsync(cook);
        Assert.Equal((true, true), Seen(await kitchen.ReloadAsync(cook), IronIngotRecipe));

        // Left, then the take arrives: the machine keeps the recipe.
        var leaver = await kitchen.JoinAsync("Leaver");
        Cooked(furnace, IronIngotRecipe);
        await kitchen.OpenAsync(furnace.Furnace, leaver);
        await leaver.StopTickingAsync();
        await kitchen.TakeResultAsync(leaver);
        await leaver.SaveAsync();

        Assert.Equal((false, false), Seen(await kitchen.ReloadAsync(leaver), IronIngotRecipe));
        Assert.Equal(1, furnace.RecipesUsed.Values.Sum());

        // A craft recorded before leaving fires recipe_crafted before the leave's save.
        var crafter = await kitchen.JoinAsync("Crafter");
        crafter.RecordProgressCraft(CraftingTableRecipe, new ItemStack(ItemsRegistry.CraftingTable, 1));
        await LeaveAsync(crafter);

        var reloaded = await kitchen.ReloadAsync(crafter);
        Assert.True(reloaded.Advancements.HasCriterion(RecipeAwards.CraftedAdvancement(CraftingTableRecipe), "fired"));
    }

    // The machine breaking right after a take finds nothing left to drop: the taker was given it with the output.
    [Fact(DisplayName = "A machine broken right after a take leaves the taker its recipes",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task BreakingAMachineAfterATakeLeavesTheTakerItsRecipes()
    {
        await using var kitchen = await Kitchen.OpenAsync("taking and breaking");
        var furnace = await kitchen.PlaceAsync<FurnaceBlockEntity>(BlocksRegistry.Furnace);
        var cook = kitchen.Player;
        await kitchen.TakeCookedAsync(furnace, cook, IronIngotRecipe);

        await kitchen.World.SetBlockAsync(position, BlocksRegistry.Air);
        await kitchen.World.DoWorldTickAsync();
        await cook.StopTickingAsync();

        Assert.Equal((true, true), Seen(cook, IronIngotRecipe));
    }

    // One recipe's reward throwing is logged; it isn't left known, and the batch's other recipe is still given.
    [Fact(DisplayName = "A recipe whose reward throws doesn't stop the batch's other recipes",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task ARecipeWhoseRewardThrowsLeavesTheOthersGiven()
    {
        await using var awards = RecipeAwards.Apply(
            RecipeAwards.Rewarding(GlassRecipe, """{ "loot": ["probe:throwing"] }"""),
            RecipeAwards.Crafted(IronIngotRecipe),
            RecipeAwards.Crafted(GlassRecipe));
        PluginLootTables.Apply([new LootTable { Identifier = "probe:throwing", Pools = [ThrowingPool()] }]);
        try
        {
            await using var kitchen = await Kitchen.OpenAsync("throwing reward");
            var furnace = await kitchen.PlaceAsync<FurnaceBlockEntity>(BlocksRegistry.Furnace);
            var cook = kitchen.Player;
            await kitchen.TakeCookedAsync(furnace, cook, GlassRecipe, IronIngotRecipe);
            await cook.StopTickingAsync();

            Assert.Equal((true, true), Seen(cook, IronIngotRecipe));
            Assert.False(Seen(cook, GlassRecipe).Known);
            Assert.True(cook.Advancements.HasCriterion(RecipeAwards.CraftedAdvancement(IronIngotRecipe), "fired"));
            Assert.True(cook.Advancements.HasCriterion(RecipeAwards.CraftedAdvancement(GlassRecipe), "fired"));
        }
        finally
        {
            PluginLootTables.Apply([]);
        }
    }

    // A recipe in the batch that an earlier recipe's reward unlocks is known with its criterion met, once.
    [Fact(DisplayName = "A cooked recipe another recipe's reward unlocks is known with its criterion",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task ARecipeUnlockedByAnotherRecipesRewardIsKnownWithItsCriterion()
    {
        await using var awards = RecipeAwards.Apply(
            RecipeAwards.Rewarding(IronIngotRecipe, $$"""{ "recipes": ["{{GlassRecipe}}"] }"""));
        await using var kitchen = await Kitchen.OpenAsync("nested unlock");
        var furnace = await kitchen.PlaceAsync<FurnaceBlockEntity>(BlocksRegistry.Furnace);
        var cook = kitchen.Player;
        await kitchen.TakeCookedAsync(furnace, cook, IronIngotRecipe, GlassRecipe);
        await cook.StopTickingAsync();

        Assert.Equal((true, true), Seen(cook, IronIngotRecipe));
        Assert.Equal((true, true), Seen(cook, GlassRecipe));
    }

    private const string IronIngotRecipe = "minecraft:iron_ingot_from_smelting_raw_iron";
    private const string GlassRecipe = "minecraft:glass";
    private const string CraftingTableRecipe = "minecraft:crafting_table";

    // The vanilla recipe advancements whose recipe_unlocked criterion each recipe meets.
    private static readonly Dictionary<string, string> recipeAdvancements = new()
    {
        [IronIngotRecipe] = "minecraft:recipes/misc/iron_ingot_from_smelting_raw_iron",
        [GlassRecipe] = "minecraft:recipes/building_blocks/glass"
    };

    // The machine cooked each recipe once, and has a result to take.
    private static void Cooked(FurnaceBlockEntity furnace, params string[] recipes)
    {
        var set = ((AbstractLevel)furnace.Level!).CookingRecipes;
        foreach (var recipe in recipes)
        {
            var key = set.Recipes.Keys.Single(key => AdvancementsRegistry.Qualify(key) == recipe);
            furnace.RecipesUsed[key] = 1;
        }

        furnace.Inventory[2] = new ItemStack(ItemsRegistry.IronIngot, 1);
    }

    // Whether the recipe is known, as the player's save writes it, and whether its recipe advancement's
    // recipe_unlocked criterion is met.
    private static (bool Known, bool Criterion) Seen(Obsidian.Entities.Player player, string recipe)
    {
        var known = player.SaveData().TryGetTag<NbtCompound>("recipeBook", out var book)
            && book.TryGetTag<NbtList>("recipes", out var recipes)
            && recipes.OfType<NbtTag<string>>().Any(known => known.Value == recipe);
        var criterion = player.Advancements.HasCriterion(recipeAdvancements[recipe], "has_the_recipe");

        return (known, criterion);
    }

    // What a leave does to the player: their ticks stop, then they're saved.
    private static async Task LeaveAsync(Obsidian.Entities.Player player)
    {
        await player.StopTickingAsync();
        await player.SaveAsync();
    }

    // A pool whose condition throws, so that the table's rolls throw.
    private static LootPool ThrowingPool() => new()
    {
        Entries = [],
        Rolls = new ConstantNumber { Value = 1 },
        Conditions = [new ThrowingCondition()]
    };

    private sealed class ThrowingCondition : ILootCondition
    {
        public bool Test(LootContext context) => throw new InvalidOperationException("A probe reward that throws.");
    }

    /// <summary>
    /// Advancements a test adds for a while: one met when recipe_crafted fires for a recipe, or one met when
    /// recipe_unlocked fires for a recipe with the given reward.
    /// </summary>
    private sealed class RecipeAwards : IAsyncDisposable
    {
        private readonly API.Advancements.Advancement[] previous;

        private RecipeAwards(API.Advancements.Advancement[] previous)
        {
            this.previous = previous;
        }

        public static RecipeAwards Apply(params API.Advancements.Advancement[] added)
        {
            var previous = AdvancementsRegistry.Current.Values.ToArray();
            AdvancementsRegistry.Apply([.. previous, .. added]);

            return new RecipeAwards(previous);
        }

        public static string CraftedAdvancement(string recipe) => $"probe:crafted/{Path(recipe)}";

        public static API.Advancements.Advancement Crafted(string recipe) => Parse(CraftedAdvancement(recipe), $$"""
            {
              "criteria": {
                "fired": { "trigger": "minecraft:recipe_crafted", "conditions": { "recipes": "{{recipe}}" } }
              }
            }
            """);

        public static API.Advancements.Advancement Rewarding(string recipe, string rewards) =>
            Parse($"probe:rewarding/{Path(recipe)}", $$"""
            {
              "criteria": {
                "fired": { "trigger": "minecraft:recipe_unlocked", "conditions": { "recipes": "{{recipe}}" } }
              },
              "rewards": {{rewards}}
            }
            """);

        public ValueTask DisposeAsync()
        {
            AdvancementsRegistry.Apply(this.previous);
            return ValueTask.CompletedTask;
        }

        private static string Path(string recipe) => recipe["minecraft:".Length..];

        private static API.Advancements.Advancement Parse(string identifier, string json)
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return AdvancementsRegistry.Parse(identifier, document.RootElement);
        }
    }

    // A stack given its own minecraft:cooking_fuel, burning for the given time at normal speed.
    // Burning one fuel leaves exactly one crafting remainder, in the fuel slot once the stack is used up and dropped
    // otherwise, and never replaces fuel still left. Fuel that burns for no time isn't used at all.
    [Fact(DisplayName = "A furnace keeps the fuel it doesn't burn and one remainder of what it does",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task FuelLeavesOneRemainderWithoutLosingFuel()
    {
        FuelRemainder[] cases =
        [
            new("milk bucket", ItemsRegistry.MilkBucket, Count: 1, BurnTime: 1600, ItemsRegistry.Bucket, 1, null),
            new("honey bottles", ItemsRegistry.HoneyBottle, Count: 2, BurnTime: 1600, ItemsRegistry.HoneyBottle, 1,
                ItemsRegistry.GlassBottle),
            new("fuel without burn time", ItemsRegistry.Dirt, Count: 1, BurnTime: -1, ItemsRegistry.Dirt, 1, null)
        ];

        await FixtureCases.AssertAllAsync(cases, fuel => fuel.Name, AssertFuelLeavesOneRemainder);
    }

    private static async Task AssertFuelLeavesOneRemainder(FuelRemainder fuel)
    {
        await using var kitchen = await Kitchen.OpenAsync("fuel remainder");
        var furnace = await kitchen.PlaceAsync<FurnaceBlockEntity>(BlocksRegistry.Furnace);
        furnace.Inventory[0] = new ItemStack(ItemsRegistry.RawIron, 1);
        furnace.Inventory[1] = CookingFuel(fuel.Item, fuel.Count, fuel.BurnTime);

        await kitchen.TickAsync(furnace, 1);

        var inSlot = furnace.Inventory[1];
        var dropped = kitchen.Entities()
            .OfType<ItemEntity>()
            .Select(entity => entity.Item)
            .ToArray();
        Assert.True(inSlot?.Is(fuel.InSlot), $"The fuel slot holds {inSlot?.Holder.UnlocalizedName ?? "nothing"}.");
        Assert.Equal(fuel.InSlotCount, inSlot?.Count);
        Assert.Equal(1, furnace.Inventory[0]?.Count);
        if (fuel.Dropped is Item item)
        {
            var remainder = Assert.Single(dropped);
            Assert.True(remainder.Is(item));
            Assert.Equal(1, remainder.Count);
        }
        else
        {
            Assert.Empty(dropped);
        }
    }

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

    private static DecoratedPotBlockEntity DecoratedPotHolding(NbtCompound item)
    {
        var potItem = DataBlockEntity.Copy(item);
        potItem.Name = "item";
        potItem.Remove("Slot");

        var pot = new DecoratedPotBlockEntity(position);
        pot.Load(new NbtCompound { potItem });

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
    /// A fuel row: the fuel and how much, how long one burns, and what's in the fuel slot and dropped after a tick.
    /// </summary>
    private sealed record FuelRemainder(
        string Name,
        Item Item,
        int Count,
        int BurnTime,
        Item InSlot,
        int InSlotCount,
        Item? Dropped);

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

        public ValueTask OpenAsync(BaseContainer container, Obsidian.Entities.Player? player = null) =>
            (player ?? this.Player).OpenInventoryAsync(container);

        // Another player, loaded (so that their saves are written) and joined beside the first.
        public async Task<Obsidian.Entities.Player> JoinAsync(string name)
        {
            var player = TestServerFactory.CreateConnectedPlayer(this.context.Server, this.World, name,
                (VectorD)position);
            await player.LoadAsync(loadFromPersistentWorld: false);
            await TestServerFactory.JoinConnectedPlayerAsync(this.context.Server, player);

            return player;
        }

        // The player as their save loads them again.
        public async Task<Obsidian.Entities.Player> ReloadAsync(Obsidian.Entities.Player player)
        {
            var reloaded = TestServerFactory.CreateConnectedPlayer(this.context.Server, this.World,
                player.Username, (VectorD)position, player.Uuid);
            await reloaded.LoadAsync(loadFromPersistentWorld: false);

            return reloaded;
        }

        // The machine cooked the recipes; the player opens it and takes its result.
        public async Task TakeCookedAsync(FurnaceBlockEntity furnace, Obsidian.Entities.Player player,
            params string[] recipes)
        {
            Cooked(furnace, recipes);
            await this.OpenAsync(furnace.Furnace, player);
            await this.TakeResultAsync(player);
        }

        // The menu data packets the player's open container got.
        public int ContainerDataPackets()
        {
            var client = (TestClient)this.Player.Client;

            return client.SentPackets
                .OfType<ContainerSetDataPacket>()
                .Count(packet => packet.ContainerId == this.Player.CurrentContainerId);
        }

        // Clicks the open furnace's result slot, as the client sends it.
        public async Task TakeResultAsync(Obsidian.Entities.Player? player = null)
        {
            player ??= this.Player;
            using var writer = new NetworkBuffer();
            writer.WriteVarInt(player.CurrentContainerId);
            writer.WriteVarInt(0);
            writer.WriteShort(2);
            writer.WriteByte(0);
            writer.WriteVarInt((int)ClickType.Pickup);
            writer.WriteVarInt(0);
            writer.WriteBoolean(false);

            using var reader = new NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());
            var packet = new ContainerClickPacket();
            packet.Populate(reader);
            await packet.HandleAsync(player.Server, player);
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
