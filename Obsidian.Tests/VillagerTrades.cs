using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Trades = Obsidian.Entities.VillagerTrades;

namespace Obsidian.Tests;

public sealed class VillagerTrades
{
    // Vanilla 26.3's offers for each merchant of two worlds, merchants in order (TradeFixtures.java).
    private static readonly Lazy<JsonDocument> capture = new(() =>
    {
        var assembly = typeof(Server).Assembly;
        using var stream = assembly.GetManifestResourceStream("Obsidian.Assets.villager-trades-26.3.json")!;
        return JsonDocument.Parse(stream);
    });

    [Fact]
    public void OffersMatchVanillaTradeSetDraws()
    {
        foreach (var world in capture.Value.RootElement.EnumerateArray())
        {
            // The world's merchants share its random sequences, so each continues where the previous one left off.
            var seed = world.GetProperty("seed").GetInt64();
            var sequences = new RandomSequences(seed);

            foreach (var merchant in world.GetProperty("merchants").EnumerateArray())
            {
                var expected = merchant.GetProperty("levels").EnumerateArray()
                    .Select(level => level.EnumerateArray().Select(DescribeCaptured).ToList())
                    .ToList();

                var actual = Draw(merchant, sequences);

                var profession = merchant.GetProperty("profession").GetString();
                Assert.Equal(expected.Count, actual.Count);
                for (var level = 0; level < expected.Count; level++)
                {
                    var label = $"{seed} {profession} level {level + 1}";
                    Assert.Equal(Label(label, expected[level]), Label(label, actual[level]));
                }
            }
        }
    }

    [Fact]
    public void SavedOffersKeepComponentsUsesAndPricesAcrossReloads()
    {
        // Librarian offers sell enchanted books for emeralds and a book; the trader buys a water bottle, a cost with a
        // component. Uses, demand and discount change as the offers are traded.
        var offers = LibrarianOffers(seed: 12345);
        offers.Add(WaterBottleOffer());
        foreach (var (offer, index) in offers.Select((offer, index) => (offer, index)))
        {
            offer.UsedCount = index;
            offer.Demand = index - 2;
            offer.Discount = -index;
            offer.RewardExp = index % 2 == 0;
            offer.IsDisabled = offer.UsedCount >= offer.MaxCount;
        }

        var restored = Reload([.. offers]);

        Assert.Equal(offers.Select(Describe), restored.Select(Describe));
        Assert.Equal(offers.Select(Progress), restored.Select(Progress));
    }

    [Theory(Timeout = TestServerFactory.TestTimeout)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VanillaSavedOffersSurviveLoadingAndFirstInteraction(bool empty)
    {
        await using var context = TestServerFactory.CreateServer($"vanilla-offers-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        using var stream = typeof(Server).Assembly.GetManifestResourceStream("Obsidian.Assets.villager-saved-26.3.nbt")!;
        var saved = Assert.IsType<NbtCompound>(new NbtReader(stream).ReadNextTag());
        var vanillaOffers = Assert.IsType<NbtCompound>(saved["Offers"]);
        if (empty)
        {
            vanillaOffers.Remove("Recipes");
            vanillaOffers.Add(new NbtList(NbtTagType.Compound, "Recipes"));
        }

        var recipes = Assert.IsType<NbtList>(vanillaOffers["Recipes"]).OfType<NbtCompound>().ToList();
        var villager = NewVillager(context.World, context.World.LevelData.SpawnPosition);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Customer");

        villager.ReadNbt(saved);
        villager.Position = player.Position;
        var loaded = MerchantOffers.Read(EntityNbt.Save(villager)!);
        await villager.InteractAsync(player, InteractionHand.MainHand);
        var afterInteraction = EntityNbt.Save(villager)!;

        Assert.Equal(recipes.Count, loaded.Length);
        for (var index = 0; index < recipes.Count; index++)
        {
            AssertSavedVanillaOffer(recipes[index], loaded[index]);
        }

        Assert.Equal(5, afterInteraction.GetInt("ObsidianTradingLevel"));
        Assert.Equal(loaded.Select(Describe), MerchantOffers.Read(afterInteraction).Select(Describe));
        if (empty)
        {
            Assert.Null(player.OpenedContainer);
        }
        else
        {
            var shown = ((TestClient)player.Client).SentPackets
                .OfType<Obsidian.Net.Packets.Play.Clientbound.MerchantOffersPacket>().Last();
            Assert.Equal(loaded.Select(Describe), shown.Offers.Select(Describe));
            Assert.Equal(loaded.Select(Progress), Reload(loaded).Select(Progress));
        }
    }

    private static void AssertSavedVanillaOffer(NbtCompound saved, TradeEntry actual)
    {
        var first = Assert.IsType<NbtCompound>(saved["buy"]).ItemFromNbt()!;
        var output = Assert.IsType<NbtCompound>(saved["sell"]).ItemFromNbt()!;
        var second = saved.TryGetTag<NbtCompound>("buyB", out var buyB) ? buyB.ItemFromNbt() : null;
        Assert.Equal(Describe(first), Describe(actual.FirstInput));
        Assert.Equal(Describe(output), Describe(actual.Output));
        Assert.Equal(second is null ? "nothing" : Describe(second),
            actual.SecondInput.Count == 0 ? "nothing" : Describe(actual.SecondInput));
        Assert.Equal(saved.TryGetTagValue<int>("uses", out var uses) ? uses : 0, actual.UsedCount);
        Assert.Equal(saved.TryGetTagValue<int>("maxUses", out var maximum) ? maximum : 4, actual.MaxCount);
        Assert.Equal(saved.TryGetTagValue<int>("xp", out var xp) ? xp : 1, actual.XP);
        Assert.Equal(saved.TryGetTagValue<int>("demand", out var demand) ? demand : 0, actual.Demand);
        Assert.Equal(saved.TryGetTagValue<int>("specialPrice", out var specialPrice) ? specialPrice : 0, actual.Discount);
        Assert.Equal(saved.TryGetTagValue<float>("priceMultiplier", out var multiplier) ? multiplier : 0,
            actual.Multiplier);
        Assert.Equal(!saved.TryGetBool("rewardExp", out var reward) || reward, actual.RewardExp);
        Assert.Equal(actual.UsedCount >= actual.MaxCount, actual.IsDisabled);
    }

    [Theory(Timeout = TestServerFactory.TestTimeout)]
    [InlineData("disconnect")]
    [InlineData("server_close")]
    [InlineData("dimension_change")]
    [InlineData("replacement")]
    public async Task MerchantInputsSurviveEveryWindowExit(string exit)
    {
        await using var context = TestServerFactory.CreateServer($"trade-exit-{Guid.NewGuid():N}");
        await using var destination = TestServerFactory.CreateServer($"trade-target-{Guid.NewGuid():N}");
        var nether = AddNether(destination);
        AddTravelChunks(nether);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Customer");
        await player.LoadAsync(loadFromPersistentWorld: false);
        context.World.TryAddPlayer(player);
        context.World.TryAddEntity(player);
        player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.Emerald, 64));
        player.Inventory.SetItem(10, new ItemStack(ItemsRegistry.Book, 3));
        var villager = NewVillager(context.World, player.Position);
        var offer = LibrarianOffers(seed: 12345).First(offer => offer.SecondInput.Count > 0);
        await MerchantTrading.OpenAsync(villager, player, [offer], 1, 0, canRestock: true);
        var merchant = Assert.IsType<MerchantContainer>(player.OpenedContainer);
        await merchant.SelectAsync(0);
        Assert.Equal(0, Count(player, ItemsRegistry.Emerald));
        Assert.Equal(0, Count(player, ItemsRegistry.Book));

        switch (exit)
        {
            case "disconnect":
                await player.DisconnectAsync(ChatMessage.Simple("Leaving"));
                await context.EventDispatcher.ExecuteEventAsync(new Obsidian.API.Events.PlayerLeaveEventArgs(
                    player, context.Server, DateTimeOffset.UtcNow));
                break;
            case "server_close":
                await ((IPlayer)player).CloseContainerAsync(null);
                break;
            case "dimension_change":
                await player.TeleportAsync(nether, new VectorD(0.5, 64, 0.5));
                Assert.Same(nether, player.Level);
                break;
            case "replacement":
                await player.OpenInventoryAsync(new Container(9));
                break;
        }

        Assert.Equal(64, Count(player, ItemsRegistry.Emerald));
        Assert.Equal(3, Count(player, ItemsRegistry.Book));
        Assert.Null(merchant.GetItem(0));
        Assert.Null(merchant.GetItem(1));
        if (exit == "server_close")
        {
            var inventory = ((TestClient)player.Client).SentPackets.OfType<ContainerSetContentPacket>()
                .Last(packet => packet.ContainerId == 0);
            Assert.Equal(64, inventory.Items[9]?.Count);
            Assert.Equal(3, inventory.Items[10]?.Count);
        }

        // Closing again must not duplicate items, and recovery must precede the disconnect/travel save.
        await merchant.CloseAsync(player);
        if (exit is "server_close" or "replacement")
            await player.SaveAsync();

        var world = player.Level is Obsidian.API.IDimension dimension
            ? dimension.ParentWorld
            : Assert.IsAssignableFrom<IWorld>(player.Level);
        var restored = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Customer", uuid: player.Uuid);
        await restored.LoadAsync(loadFromPersistentWorld: false);
        Assert.Equal(64, Count(restored, ItemsRegistry.Emerald));
        Assert.Equal(3, Count(restored, ItemsRegistry.Book));
    }

    private static Obsidian.WorldData.Dimension AddNether(RealServerContext context)
    {
        var dimension = new Obsidian.WorldData.Dimension(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Obsidian.WorldData.Dimension>.Instance,
            context.Broadcaster,
            new TestOptionsMonitor<Obsidian.API.Configuration.ServerConfiguration>(context.World.Configuration),
            context.EventDispatcher,
            new Obsidian.WorldData.Generators.EmptyWorldGenerator(),
            "minecraft:the_nether",
            context.World);
        dimension.Initialize(CodecRegistry.Dimensions.All["minecraft:the_nether"]);
        context.World.RegisterDimension(dimension);
        return dimension;
    }

    private static void AddTravelChunks(Obsidian.WorldData.AbstractLevel world)
    {
        for (var x = -3; x <= 3; x++)
        {
            for (var z = -3; z <= 3; z++)
            {
                var chunk = new Obsidian.WorldData.Chunk(x, z, world.MinY, world.Height,
                    ChunkGenStage.full);
                world.LoadRegionByChunk(x, z).SetChunk(chunk);
            }
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task TradingTakesTheCostsOnceAndGivesTheDrawnItem()
    {
        await using var context = TestServerFactory.CreateServer($"trades-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Customer");
        var offer = LibrarianOffers(seed: 12345).First(offer => offer.SecondInput.Count > 0);
        var price = offer.FirstInput.Count;
        player.Inventory.SetItem(9, new ItemStack(ItemsRegistry.Emerald, 64));
        player.Inventory.SetItem(10, new ItemStack(ItemsRegistry.Book, 3));
        var villager = NewVillager(context.World, player.Position);

        await MerchantTrading.OpenAsync(villager, player, [offer], 1, 0, canRestock: true);
        var merchant = Assert.IsType<MerchantContainer>(player.OpenedContainer);
        await merchant.SelectAsync(0);
        await MenuClicks.ClickAsync(context.Server, player, 2, 0, ClickType.Pickup, StateId(player));
        await merchant.CloseAsync(player);

        Assert.Equal(Describe(offer.Output), Describe(player.CarriedItem!));
        Assert.Equal(64 - price, Count(player, ItemsRegistry.Emerald));
        Assert.Equal(2, Count(player, ItemsRegistry.Book));
        Assert.Equal(1, offer.UsedCount);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task CostComponentsMustMatchExactly()
    {
        await using var context = TestServerFactory.CreateServer($"trade-costs-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Customer");
        var offer = WaterBottleOffer();
        var villager = NewVillager(context.World, player.Position);
        player.Inventory.SetItem(9, Potion(Obsidian.API.Potion.Awkward));

        // An awkward potion isn't the water bottle the offer wants, so nothing is sold.
        await MerchantTrading.OpenAsync(villager, player, [offer], 1, 0, canRestock: true);
        var merchant = Assert.IsType<MerchantContainer>(player.OpenedContainer);
        await merchant.SelectAsync(0);
        await MenuClicks.ClickAsync(context.Server, player, 2, 0, ClickType.Pickup, StateId(player));
        await merchant.CloseAsync(player);

        Assert.Null(player.CarriedItem);
        Assert.Equal(0, offer.UsedCount);
        Assert.Equal(1, Count(player, ItemsRegistry.Potion));
    }

    // Drawing reads only the merchant, so its level is left out.
    private static List<List<string>> Draw(JsonElement merchant, RandomSequences sequences)
    {
        var profession = merchant.GetProperty("profession").GetString()!;
        var levels = new List<List<string>>();
        if (profession == "wandering_trader")
        {
            var trader = new WanderingTrader { Level = null! };
            var offers = new List<TradeEntry>();
            Trades.AddOffers(Trades.WanderingTraderBuying, trader, sequences, null, offers);
            Trades.AddOffers(Trades.WanderingTraderUncommon, trader, sequences, null, offers);
            Trades.AddOffers(Trades.WanderingTraderCommon, trader, sequences, null, offers);
            levels.Add(offers.Select(Describe).ToList());
            return levels;
        }

        var type = merchant.GetProperty("type").GetString()!["minecraft:".Length..];
        var villager = new Villager
        {
            Level = null!,
            VillagerProfession = Array.IndexOf(Obsidian.Entities.Villager.Professions, profession),
            VillagerType = Array.IndexOf(Obsidian.Entities.Villager.BiomeTypes, type)
        };
        for (var level = 1; level <= merchant.GetProperty("levels").GetArrayLength(); level++)
        {
            var offers = new List<TradeEntry>();
            Trades.AddOffers(Trades.LevelTradeSet(profession, level), villager, sequences, null, offers);
            levels.Add(offers.Select(Describe).ToList());
        }

        return levels;
    }

    private static List<TradeEntry> LibrarianOffers(long seed)
    {
        var librarian = new Villager { Level = null!, VillagerProfession = 9 };
        var sequences = new RandomSequences(seed);
        var offers = new List<TradeEntry>();
        for (var level = 1; level <= 5; level++)
        {
            Trades.AddOffers(Trades.LevelTradeSet("librarian", level), librarian, sequences, null, offers);
        }

        return offers;
    }

    // Like the wandering trader's: a water bottle for an emerald.
    private static TradeEntry WaterBottleOffer() => new()
    {
        FirstInput = new TradeItem(ItemsRegistry.Potion.Id, 1,
            [new PotionContentsDataComponent { Potion = Obsidian.API.Potion.Water }]),
        SecondInput = new TradeItem(ItemsRegistry.Air.Id, 0, []),
        Output = new ItemStack(ItemsRegistry.Emerald),
        MaxCount = 2,
        Multiplier = 0.05f
    };

    private static ItemStack Potion(Potion potion) =>
        new(ItemsRegistry.Potion, 1, new PotionContentsDataComponent { Potion = potion });

    private static Villager NewVillager(IWorld world, VectorD position) => new()
    {
        Level = world,
        EntityId = Server.GetNextEntityId(),
        Position = position,
        Health = 20
    };

    private static ImmutableArray<TradeEntry> Reload(ImmutableArray<TradeEntry> offers)
    {
        using var writer = new RawNbtWriter("");
        MerchantOffers.Write(writer, offers);
        writer.EndCompound();

        using var stream = new MemoryStream(writer.Data.ToArray());
        var root = (NbtCompound)new NbtReader(stream).ReadNextTag()!;
        return MerchantOffers.Read(root);
    }

    private static int StateId(Obsidian.Entities.Player player) =>
        ((TestClient)player.Client).SentPackets
            .OfType<Obsidian.Net.Packets.Play.Clientbound.ContainerSetContentPacket>().Last().StateId;

    private static int Count(Obsidian.Entities.Player player, Item item)
    {
        var carried = player.CarriedItem is { } cursor && cursor.Holder == item ? cursor.Count : 0;
        var stored = Enumerable.Range(9, 36)
            .Select(player.Inventory.GetItem)
            .Where(stack => stack is not null && stack.Holder == item)
            .Sum(stack => stack!.Count);
        return carried + stored;
    }

    private static string Label(string merchant, List<string> offers) => $"{merchant}: {string.Join(" | ", offers)}";

    private static string Progress(TradeEntry offer) =>
        $"{offer.UsedCount}/{offer.MaxCount} demand {offer.Demand} discount {offer.Discount} " +
        $"{offer.IsDisabled} reward {offer.RewardExp}";

    // An offer as vanilla's MerchantOffer.CODEC wrote it, with its defaults (4 uses, 1 experience).
    private static string DescribeCaptured(JsonElement offer)
    {
        var buyB = offer.TryGetProperty("buyB", out var second) ? DescribeCapturedStack(second) : "nothing";
        var maxUses = offer.TryGetProperty("maxUses", out var uses) ? uses.GetInt32() : 4;
        var xp = offer.TryGetProperty("xp", out var experience) ? experience.GetInt32() : 1;
        var multiplier = offer.TryGetProperty("priceMultiplier", out var price) ? price.GetSingle() : 0;

        var sell = DescribeCapturedStack(offer.GetProperty("sell"));
        var buy = DescribeCapturedStack(offer.GetProperty("buy"));
        return Describe(buy, buyB, sell, maxUses, xp, multiplier);
    }

    private static string DescribeCapturedStack(JsonElement stack)
    {
        var saved = VanillaLoot.CapturedCompound("", stack);
        return Describe(saved.ItemFromNbt()!);
    }

    private static string Describe(TradeEntry offer)
    {
        var buy = Describe(offer.FirstInput);
        var buyB = offer.SecondInput.Count > 0 ? Describe(offer.SecondInput) : "nothing";
        return Describe(buy, buyB, Describe(offer.Output), offer.MaxCount, offer.XP, offer.Multiplier);
    }

    private static string Describe(string buy, string buyB, string sell, int maxUses, int xp, float multiplier)
    {
        var price = multiplier.ToString(CultureInfo.InvariantCulture);
        return $"{buy} + {buyB} -> {sell} (uses {maxUses}, xp {xp}, multiplier {price})";
    }

    private static string Describe(TradeItem cost) =>
        Describe(new ItemStack(ItemsRegistry.Get(cost.ID), cost.Count, cost.Components));

    // The stack as saved, in a compact SNBT-like form.
    private static string Describe(ItemStack stack) => Describe(stack.ToNbt());

    private static string Describe(INbtTag tag)
    {
        // Compound entries are unordered (vanilla's enchantment maps are in hash order), so they're sorted.
        if (tag is NbtCompound compound)
        {
            var entries = compound.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => $"{entry.Key}:{Describe(entry.Value)}");
            return "{" + string.Join(",", entries) + "}";
        }

        if (tag is NbtList list)
            return "[" + string.Join(",", list.Select(Describe)) + "]";

        // A value's text follows its type and name.
        var text = tag.ToString()!;
        return text[(text.IndexOf("): ", StringComparison.Ordinal) + 3)..];
    }
}
