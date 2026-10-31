using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Commands.Framework;
using Obsidian.Events;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Brewing stands: shift-clicking routes stacks to their slots like vanilla's brewing menu, they brew what vanilla's
/// brewing recipes brew (a capture), and plugins' mixes brew until their plugin unloads.
/// </summary>
// Loading the probe plugin merges its tags into vanilla's, which PluginTagFiles changes too.
[Collection(nameof(PluginTagFiles))]
public sealed class BrewingStands : IDisposable
{
    private static readonly FakeServer server = new();

    // The data slot ids vanilla 26.3's BrewingStandMenu reads (the ContainerData.get call in each of its getters),
    // with the stand value each getter shows.
    private static readonly (string Getter, short Id, Func<BrewingStand, int> Value)[] menuData =
    [
        ("getBrewingTicks", 0, stand => stand.BrewTime),
        ("getFuel", 1, stand => stand.Fuel),
        ("getTotalBrewingTicks", 2, stand => stand.TotalBrewTime),
        ("getTotalFuel", 3, stand => stand.TotalFuel)
    ];

    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-brewing-{Guid.NewGuid():N}");

    public BrewingStands() => Directory.CreateDirectory(this.folder);

    public void Dispose() => Directory.Delete(this.folder, recursive: true);

    [Fact(DisplayName = "Brewing stands brew what vanilla 26.3's brewing recipes brew")]
    public void BrewsLikeVanilla()
    {
        using var stream = typeof(Server).Assembly.GetManifestResourceStream("Obsidian.Assets.brewing-26.3.json")!;
        using var capture = JsonDocument.Parse(stream);
        var root = capture.RootElement;

        // Vanilla's brews by bottle, potion and reagent; every other combination brews nothing.
        var expected = new Dictionary<(string Bottle, string Potion, string Reagent), string>();
        foreach (var brew in root.GetProperty("brews").EnumerateArray())
        {
            var key = (Text(brew, "bottle"), Text(brew, "potion"), Text(brew, "reagent"));
            expected.Add(key, Describe(brew.GetProperty("result")));
        }

        var mismatches = new List<string>();
        foreach (var bottle in Names(root, "bottles"))
        {
            foreach (var potion in Names(root, "potions"))
            {
                foreach (var reagent in Names(root, "reagents"))
                {
                    var input = Potion(ItemsRegistry.Get(bottle), ParsePotion(potion));
                    var ingredient = new ItemStack(ItemsRegistry.Get(reagent), 1);
                    var brewed = Brewing.Mix(input, ingredient, RecipesRegistry.Current);

                    var wanted = expected.GetValueOrDefault((bottle, potion, reagent), "nothing");
                    var actual = brewed is null ? "nothing" : Describe(brewed);
                    if (wanted != actual)
                        mismatches.Add($"{bottle} of {potion} with {reagent}: vanilla {wanted}, Obsidian {actual}");
                }
            }
        }

        Assert.Empty(mismatches);
    }

    [Fact(DisplayName = "A brewing stand brews with its fuel and resumes saved progress")]
    public void BrewingCompletesAndResumesSavedProgress()
    {
        // Two water bottles, nether wart and one blaze powder.
        var stand = new BrewingStand();
        stand.SetItem(0, Potion(ItemsRegistry.Potion, API.Potion.Water));
        stand.SetItem(2, Potion(ItemsRegistry.Potion, API.Potion.Water));
        stand.SetItem(BrewingStand.IngredientSlot, new ItemStack(ItemsRegistry.NetherWart, 2));
        stand.SetItem(BrewingStand.FuelSlot, new ItemStack(ItemsRegistry.BlazePowder, 1));

        // The first tick burns the blaze powder for 20 brews and starts one, using a brew of it up.
        var first = Tick(stand);

        Assert.Null(stand.GetItem(BrewingStand.FuelSlot));
        Assert.Equal(19, stand.Fuel);
        Assert.Equal(20, stand.TotalFuel);
        Assert.Equal(400, stand.BrewTime);
        Assert.Equal(400, stand.TotalBrewTime);
        Assert.Equal([true, false, true], first.Bottles);

        // 400 ticks later both bottles are awkward potions and one nether wart is left.
        Assert.Equal(400, TicksUntilBrewed(stand));

        Assert.Equal(API.Potion.Awkward, PotionOf(stand.GetItem(0)));
        Assert.Equal(API.Potion.Awkward, PotionOf(stand.GetItem(2)));
        Assert.Null(stand.GetItem(1));
        Assert.Equal(1, stand.GetItem(BrewingStand.IngredientSlot)?.Count);

        // A brew saved with 100 ticks left finishes 100 ticks after loading, without using more fuel.
        var loaded = LoadStand(brewTime: 100, fuel: 5);

        Assert.Equal(100, TicksUntilBrewed(loaded));

        Assert.Equal(API.Potion.Awkward, PotionOf(loaded.GetItem(0)));
        Assert.Equal(5, loaded.Fuel);
    }

    [Fact(DisplayName = "An open brewing stand's menu gets its values under vanilla's data slot ids",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task MenuDataUsesVanillaIds()
    {
        await using var context = TestServerFactory.CreateServer($"brewing-menu-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Brewer", VectorD.Zero);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);

        // Every value differs, so a value under the wrong id shows.
        var stand = new BrewingStand
        {
            BrewTime = 123,
            Fuel = 7,
            TotalBrewTime = 400,
            TotalFuel = 20
        };

        await player.OpenInventoryAsync(stand);

        var sent = ((TestClient)player.Client).SentPackets
            .OfType<ContainerSetDataPacket>()
            .Where(packet => packet.ContainerId == player.CurrentContainerId)
            .ToDictionary(packet => packet.WindowProperty.Property, packet => (int)packet.WindowProperty.Value);
        var expected = menuData.ToDictionary(slot => slot.Id, slot => slot.Value(stand));

        Assert.Equal(expected, sent);
    }

    [Fact(DisplayName = "A plugin's potion mix brews until the plugin unloads",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task PluginMixBrewsUntilUnloaded()
    {
        await using var context = TestServerFactory.CreateServer($"brewing-{Guid.NewGuid():N}");
        await using var manager = new PluginManager(
            context.Server,
            context.Provider,
            context.EventDispatcher,
            context.Provider.GetRequiredService<CommandHandler>(),
            NullLogger<PluginManager>.Instance,
            new ConfigurationBuilder().Build());

        var path = Path.Join(this.folder, "probe.obby");
        await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0"));
        var container = (await manager.LoadPluginAsync(path))!;

        Assert.True(ItemsRegistry.TryGet("probe:gem", out var gem));
        var awkward = Potion(ItemsRegistry.SplashPotion, API.Potion.Awkward);
        var stand = new BrewingStand();
        var player = new FakePlayer { OpenedContainer = stand };

        // Its gem is an ingredient now: shift-clicking sends it to the ingredient slot, and it brews.
        player.Inventory.SetItem(9, new ItemStack(gem, 4));
        QuickMove(player, clicked: 9, window: 5);

        Assert.Equal(4, stand.GetItem(BrewingStand.IngredientSlot)?.Count);

        stand.SetItem(0, awkward);
        stand.Fuel = 1;
        TicksUntilBrewed(stand);

        Assert.True(stand.GetItem(0)?.Is(ItemsRegistry.SplashPotion));
        Assert.Equal(API.Potion.Luck, PotionOf(stand.GetItem(0)));

        // Unloading the plugin takes its mix away.
        await manager.UnloadPluginAsync(container);

        var unloadedGem = new ItemStack(gem, 1);
        Assert.False(Brewing.IsIngredient(unloadedGem, RecipesRegistry.Current));
        Assert.Null(Brewing.Mix(awkward, unloadedGem, RecipesRegistry.Current));
        Assert.DoesNotContain(PluginPotionMixes.PotionMixes, mix => mix.Ingredient == gem);
    }

    // Burning one fuel leaves exactly one crafting remainder, in the fuel slot once the stack is used up and dropped
    // otherwise, and never replaces fuel still left.
    [Fact(DisplayName = "A brewing stand keeps one remainder of the fuel it burns without losing the rest")]
    public void FuelLeavesOneRemainderWithoutLosingFuel()
    {
        (string Name, Item Fuel, int Count, Item InSlot, Item? Dropped)[] cases =
        [
            ("milk bucket", ItemsRegistry.MilkBucket, 1, ItemsRegistry.Bucket, null),
            ("honey bottles", ItemsRegistry.HoneyBottle, 2, ItemsRegistry.HoneyBottle, ItemsRegistry.GlassBottle)
        ];

        FixtureCases.AssertAll(
            cases,
            row => row.Name,
            row => AssertFuelLeavesOneRemainder(row.Fuel, row.Count, row.InSlot, row.Dropped));
    }

    private static void AssertFuelLeavesOneRemainder(Item fuel, int count, Item inSlot, Item? dropped)
    {
        var stand = new BrewingStand();
        stand.SetItem(BrewingStand.FuelSlot, BrewingFuel(fuel, count));

        var step = Tick(stand);

        var left = stand.GetItem(BrewingStand.FuelSlot);
        Assert.True(stand.Fuel > 0, "The stand burned no fuel.");
        Assert.True(left?.Is(inSlot), $"The fuel slot holds {left?.Holder.UnlocalizedName ?? "nothing"}.");
        Assert.Equal(1, left?.Count);
        if (dropped is Item item)
        {
            var remainder = Assert.Single(step.Remainders);
            Assert.True(remainder.Is(item));
            Assert.Equal(1, remainder.Count);
        }
        else
        {
            Assert.Empty(step.Remainders);
        }
    }

    private static Brewing.Step Tick(BrewingStand stand) => Brewing.Tick(stand, RecipesRegistry.Current);

    // Ticks the stand until a brew ends, returning how many ticks that took.
    private static int TicksUntilBrewed(BrewingStand stand)
    {
        for (var ticks = 1; ticks <= 1000; ticks++)
        {
            if (Tick(stand).Brewed)
                return ticks;
        }

        throw new InvalidOperationException("The stand didn't brew within 1000 ticks.");
    }

    // A stand loaded from a save with a water bottle and nether wart, brewing with the given time and fuel left.
    private static BrewingStand LoadStand(short brewTime, byte fuel)
    {
        var items = new NbtList(NbtTagType.Compound, "Items")
        {
            Slotted(Potion(ItemsRegistry.Potion, API.Potion.Water), 0),
            Slotted(new ItemStack(ItemsRegistry.NetherWart, 1), BrewingStand.IngredientSlot)
        };

        var tag = new NbtCompound
        {
            items,
            new NbtTag<short>("BrewTime", brewTime),
            new NbtTag<byte>("Fuel", fuel)
        };

        var entity = (BrewingStandBlockEntity)BlockEntityNbt.Load(tag, "minecraft:brewing_stand", Vector.Zero);
        return entity.Stand;
    }

    // Shift-clicks a slot of the player's inventory in the stand's window.
    private static void QuickMove(FakePlayer player, short clicked, short window)
    {
        MainEventHandler.OnInventoryClickTest(new ContainerClickEventArgs(player, server, player.Inventory)
        {
            ContainerId = 1,
            ClickedSlot = clicked,
            WindowSlot = window,
            Button = 0,
            StateId = 0,
            ClickType = ClickType.QuickMove
        });
    }

    // A stack given its own minecraft:brewing_fuel: 20 brews at normal speed, blaze powder's.
    private static ItemStack BrewingFuel(Item item, int count)
    {
        var fuel = new FuelComponentData(new ComponentIntValue(20), new ComponentFloatValue(1f));

        return new ItemStack(item, count, ComponentBuilder.BrewingFuel with { Value = fuel });
    }

    private static ItemStack Potion(Item bottle, API.Potion potion) =>
        new(bottle, 1, new PotionContentsDataComponent { Potion = potion });

    private static API.Potion ParsePotion(string name) =>
        Enum.Parse<API.Potion>(name.TrimResourceTag(keepUnderscores: true).ToPascalCase());

    private static NbtCompound Slotted(ItemStack item, int slot)
    {
        var tag = item.ToNbt();
        tag.Add(new NbtTag<byte>("Slot", (byte)slot));

        return tag;
    }

    private static API.Potion? PotionOf(ItemStack? bottle) =>
        bottle?.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents)?.Potion;

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString()!;

    private static IEnumerable<string> Names(JsonElement root, string name) =>
        root.GetProperty(name).EnumerateArray().Select(entry => entry.GetString()!);

    // A brewed bottle as its item, count and potion, from vanilla's capture.
    private static string Describe(JsonElement stack)
    {
        var contents = stack.GetProperty("components").GetProperty("minecraft:potion_contents");
        var potion = contents.GetProperty("potion").GetString();

        return $"{Text(stack, "id")} x{stack.GetProperty("count").GetInt32()} of {potion}";
    }

    // A brewed bottle as its item, count and potion, from Obsidian's stack.
    private static string Describe(ItemStack stack)
    {
        var potion = PotionOf(stack)?.ToString().ToSnakeCase();

        return $"{stack.Holder.UnlocalizedName} x{stack.Count} of minecraft:{potion}";
    }

}
