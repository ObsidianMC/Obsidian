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

    // Each row fills the stand and the player's inventory, shift-clicks a window slot and checks where things went.
    // The window numbers the stand's bottles 0 to 2, its ingredient 3 and its fuel 4, then the player's inventory
    // from 9 at 5.
    private static readonly Dictionary<string, (Action<BrewingStand, Container> Fill, short Slot,
        Action<BrewingStand, Container> Check)> quickMoves = new()
        {
            ["Blaze powder tops up the fuel and stops there"] = (
            (stand, inventory) =>
            {
                stand.SetItem(BrewingStand.FuelSlot, new ItemStack(ItemsRegistry.BlazePowder, 60));
                inventory.SetItem(9, new ItemStack(ItemsRegistry.BlazePowder, 10));
            },
            5,
            (stand, inventory) =>
            {
                Assert.Equal(64, stand.GetItem(BrewingStand.FuelSlot)?.Count);
                Assert.Null(stand.GetItem(BrewingStand.IngredientSlot));
                Assert.Equal(6, inventory.GetItem(9)?.Count);
            }
            ),
            ["Blaze powder goes to the ingredient slot when the fuel is full"] = (
            (stand, inventory) =>
            {
                stand.SetItem(BrewingStand.FuelSlot, new ItemStack(ItemsRegistry.BlazePowder, 64));
                inventory.SetItem(9, new ItemStack(ItemsRegistry.BlazePowder, 10));
            },
            5,
            (stand, inventory) =>
            {
                Assert.Equal(10, stand.GetItem(BrewingStand.IngredientSlot)?.Count);
                Assert.Null(inventory.GetItem(9));
            }
            ),
            ["An ingredient goes to the ingredient slot"] = (
            (stand, inventory) => inventory.SetItem(40, new ItemStack(ItemsRegistry.NetherWart, 5)),
            36,
            (stand, inventory) =>
            {
                Assert.True(stand.GetItem(BrewingStand.IngredientSlot)?.Is(ItemsRegistry.NetherWart));
                Assert.Null(inventory.GetItem(40));
            }
            ),
            ["An ingredient stays put when the ingredient slot is full"] = (
            (stand, inventory) =>
            {
                stand.SetItem(BrewingStand.IngredientSlot, new ItemStack(ItemsRegistry.NetherWart, 64));
                inventory.SetItem(9, new ItemStack(ItemsRegistry.NetherWart, 5));
            },
            5,
            (stand, inventory) =>
            {
                Assert.Equal(5, inventory.GetItem(9)?.Count);
                Assert.Null(inventory.GetItem(36));
            }
            ),
            ["Bottles go one to each bottle slot"] = (
            (stand, inventory) => inventory.SetItem(9, new ItemStack(ItemsRegistry.GlassBottle, 5)),
            5,
            (stand, inventory) =>
            {
                for (var slot = 0; slot < 3; slot++)
                    Assert.Equal(1, stand.GetItem(slot)?.Count);

                Assert.Equal(2, inventory.GetItem(9)?.Count);
            }
            ),
            ["Other stacks go from the main inventory to the hotbar"] = (
            (stand, inventory) => inventory.SetItem(9, new ItemStack(ItemsRegistry.Dirt, 3)),
            5,
            (stand, inventory) => Assert.Equal(3, inventory.GetItem(36)?.Count)),
            ["Other stacks go from the hotbar to the main inventory"] = (
            (stand, inventory) => inventory.SetItem(36, new ItemStack(ItemsRegistry.Dirt, 3)),
            32,
            (stand, inventory) => Assert.Equal(3, inventory.GetItem(9)?.Count)),
            ["The stand's stacks go to the hotbar's last slot first"] = (
            (stand, inventory) => stand.SetItem(1, Potion(ItemsRegistry.Potion, API.Potion.Awkward)),
            1,
            (stand, inventory) =>
            {
                Assert.Null(stand.GetItem(1));
                Assert.True(inventory.GetItem(44)?.Is(ItemsRegistry.Potion));
            }
            )
        };

    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-brewing-{Guid.NewGuid():N}");

    public BrewingStands() => Directory.CreateDirectory(this.folder);

    public void Dispose() => Directory.Delete(this.folder, recursive: true);

    public static TheoryData<string> QuickMoveRows => [.. quickMoves.Keys];

    [Theory(DisplayName = "Shift-clicking in a brewing stand routes stacks like vanilla's brewing menu")]
    [MemberData(nameof(QuickMoveRows))]
    public void QuickMoveRoutesLikeVanilla(string row)
    {
        var (fill, slot, check) = quickMoves[row];
        var stand = new BrewingStand();
        var player = new FakePlayer { OpenedContainer = stand };

        fill(stand, player.Inventory);

        var (clicked, forPlayer) = stand.GetSlot(slot);
        MainEventHandler.OnInventoryClickTest(new ContainerClickEventArgs(
            player,
            server,
            forPlayer ? player.Inventory : stand)
        {
            ContainerId = 1,
            ClickedSlot = clicked,
            WindowSlot = slot,
            Button = 0,
            StateId = 0,
            ClickType = ClickType.QuickMove
        });

        check(stand, player.Inventory);
    }

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
