using Obsidian.API.Data;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.Loot.Conditions;
using Obsidian.API.Loot.Functions;
using Obsidian.API.World.Generator.RandomSources;
using System.Collections.Frozen;
using System.Text.Json;

namespace Obsidian.Entities;

/// <summary>
/// Vanilla's data-driven trades (26.1+): the villager trades and the trade sets merchants draw their offers from,
/// read from <c>Assets/villager_trades.json</c>.
/// </summary>
/// <remarks>
/// A villager draws from its profession's set for each level it reaches (<see cref="LevelTradeSet"/>), and a wandering
/// trader from its buying, uncommon and common sets in that order. Each set draws from the world's random sequence it
/// names, which every merchant of the world continues.
/// </remarks>
internal static class VillagerTrades
{
    internal const string WanderingTraderBuying = "minecraft:wandering_trader/buying";
    internal const string WanderingTraderUncommon = "minecraft:wandering_trader/uncommon";
    internal const string WanderingTraderCommon = "minecraft:wandering_trader/common";

    private static readonly Lazy<FrozenDictionary<string, TradeSet>> tradeSets = new(Load);

    /// <summary>The trade set of a villager profession (like <c>armorer</c>) at a level from 1 to 5.</summary>
    internal static string LevelTradeSet(string profession, int level) => $"minecraft:{profession}/level_{level}";

    /// <summary>
    /// Adds the offers <paramref name="merchant"/> draws from the trade set <paramref name="tradeSetId"/> to
    /// <paramref name="offers"/>, like vanilla's <c>AbstractVillager.addOffersFromTradeSet</c>. An unknown set adds
    /// none.
    /// </summary>
    /// <param name="sequences">The world's random sequences.</param>
    /// <param name="explorationMaps">Makes explorer maps for map trades, or null where the world can't.</param>
    internal static void AddOffers(string tradeSetId, Mob merchant, RandomSequences sequences,
        IExplorationMapFactory? explorationMaps, List<TradeEntry> offers)
    {
        if (!tradeSets.Value.TryGetValue(tradeSetId, out var tradeSet))
            return;

        var random = tradeSet.RandomSequence is string sequence
            ? sequences.Get(sequence)
            : new XoroshiroRandomSource(merchant.Random.NextInt64());

        // A world's sequences are shared, so draw under the source's lock, like loot does.
        lock (random)
        {
            var context = new LootContext
            {
                Random = random,
                Origin = merchant.Position,
                ThisEntity = merchant,
                AdditionalCostComponentAllowed = true,
                EntityVillagerVariant = VillagerVariant,
                ExplorationMaps = explorationMaps
            };
            tradeSet.AddOffers(context, offers);
        }
    }

    private static string? VillagerVariant(IEntity entity)
    {
        if (entity is not Villager villager)
            return null;

        var type = Villager.BiomeTypes[Math.Clamp(villager.VillagerType, 0, Villager.BiomeTypes.Length - 1)];
        return "minecraft:" + type;
    }

    private static FrozenDictionary<string, TradeSet> Load()
    {
        var assembly = typeof(VillagerTrades).Assembly;
        using var stream = assembly.GetManifestResourceStream("Obsidian.Assets.villager_trades.json")
            ?? throw new InvalidOperationException("The villager trades asset is missing.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        var trades = new Dictionary<string, VillagerTrade>();
        foreach (var trade in root.GetProperty("trades").EnumerateObject())
        {
            trades[trade.Name] = VillagerTrade.Read(trade.Value);
        }

        var sets = new Dictionary<string, TradeSet>();
        foreach (var set in root.GetProperty("trade_sets").EnumerateObject())
        {
            sets[set.Name] = TradeSet.Read(set.Value, trades);
        }

        return sets.ToFrozenDictionary();
    }

    /// <summary>A trade set: how many offers to draw, from which trades and with which random sequence.</summary>
    private sealed record TradeSet(ImmutableArray<VillagerTrade> Trades, ContextIntProvider Amount,
        bool AllowDuplicates, string? RandomSequence)
    {
        public static TradeSet Read(JsonElement json, IReadOnlyDictionary<string, VillagerTrade> trades)
        {
            var members = json.GetProperty("trades").EnumerateArray()
                .Select(id => trades[id.GetString()!])
                .ToImmutableArray();
            var allowDuplicates = json.TryGetProperty("allow_duplicates", out var duplicates)
                && duplicates.GetBoolean();
            var sequence = json.TryGetProperty("random_sequence", out var name) ? name.GetString() : null;

            return new TradeSet(members, ContextNumberProviders.ReadInt(json.GetProperty("amount")), allowDuplicates,
                sequence);
        }

        // Matches vanilla's AbstractVillager.addOffersFromItemListings and ...WithoutDuplicates: each draw picks
        // nextInt(remaining trades). A trade without an offer (its merchant predicate failed or its item was
        // discarded) leaves the list without counting; without duplicates, a drawn trade always leaves it.
        public void AddOffers(LootContext context, List<TradeEntry> offers)
        {
            var amount = this.Amount.GetInt(context.Numbers);
            var remaining = this.Trades.ToList();
            var added = 0;
            while (added < amount && remaining.Count > 0)
            {
                var index = context.Random.NextInt(remaining.Count);
                var trade = remaining[index];
                if (!this.AllowDuplicates)
                    remaining.RemoveAt(index);

                var offer = trade.GetOffer(context);
                if (offer is null)
                {
                    if (this.AllowDuplicates)
                        remaining.RemoveAt(index);

                    continue;
                }

                offers.Add(offer);
                added++;
            }
        }
    }

    /// <summary>An item a trade wants, with a count provider and the exact components the item must have.</summary>
    private sealed record TradeCost(Item Item, ContextIntProvider Count, ImmutableArray<DataComponent> Components)
    {
        public static TradeCost Read(JsonElement json)
        {
            var item = ItemsRegistry.Get(json.GetProperty("id").GetString()!);
            var count = json.TryGetProperty("count", out var value)
                ? ContextNumberProviders.ReadInt(value)
                : ContextNumberProviders.ParseInt("1");

            return new TradeCost(item, count, ReadComponents(json));
        }

        // Vanilla's TradeCost.toItemCost: the count plus the item's additional cost, within the item's stack size.
        public TradeItem ToItemCost(LootContext context, int additionalCost)
        {
            var count = Math.Clamp(this.Count.GetInt(context.Numbers) + additionalCost, 0, this.Item.MaxStackSize);
            return new TradeItem(this.Item.Id, count, this.Components);
        }
    }

    /// <summary>
    /// A villager trade: what it wants and gives, and the offer's uses, experience and price multiplier.
    /// </summary>
    private sealed class VillagerTrade
    {
        public required TradeCost Wants { get; init; }

        public TradeCost? AdditionalWants { get; init; }

        public required Item Gives { get; init; }

        public required int GivesCount { get; init; }

        public required ImmutableArray<DataComponent> GivesComponents { get; init; }

        public ILootCondition? MerchantPredicate { get; init; }

        public LootFunction? GivenItemModifier { get; init; }

        public required ContextIntProvider MaxUses { get; init; }

        public required ContextIntProvider Xp { get; init; }

        public required ContextFloatProvider ReputationDiscount { get; init; }

        /// <summary>Enchantment ids that double the additional cost of an enchanted book that stores one.</summary>
        public FrozenSet<string>? DoubleTradePriceEnchantments { get; init; }

        public static VillagerTrade Read(JsonElement json)
        {
            var gives = json.GetProperty("gives");
            var givesCount = gives.TryGetProperty("count", out var count) ? count.GetInt32() : 1;

            return new VillagerTrade
            {
                Wants = TradeCost.Read(json.GetProperty("wants")),
                AdditionalWants = json.TryGetProperty("additional_wants", out var additional)
                    ? TradeCost.Read(additional)
                    : null,
                Gives = ItemsRegistry.Get(gives.GetProperty("id").GetString()!),
                GivesCount = givesCount,
                GivesComponents = ReadComponents(gives),
                MerchantPredicate = json.TryGetProperty("merchant_predicate", out var predicate)
                    ? LootTableJson.ReadCondition(predicate.Clone())
                    : null,
                GivenItemModifier = json.TryGetProperty("given_item_modifier", out var modifier)
                    ? LootTableJson.ReadModifier(modifier.GetRawText())
                    : null,
                MaxUses = ReadInt(json, "max_uses", "4"),
                Xp = ReadInt(json, "xp", "1"),
                ReputationDiscount = json.TryGetProperty("reputation_discount", out var discount)
                    ? ContextNumberProviders.ReadFloat(discount)
                    : ContextNumberProviders.ParseFloat("0"),
                DoubleTradePriceEnchantments = json.TryGetProperty("double_trade_price_enchantments", out var doubled)
                    ? doubled.EnumerateArray().Select(id => id.GetString()!).ToFrozenSet()
                    : null
            };
        }

        private static ContextIntProvider ReadInt(JsonElement json, string name, string fallback) =>
            json.TryGetProperty(name, out var value)
                ? ContextNumberProviders.ReadInt(value)
                : ContextNumberProviders.ParseInt(fallback);

        // Vanilla's VillagerTrade.getOffer, in its order of random draws: the modifier, then the costs, uses,
        // experience and price multiplier. Null when the merchant doesn't qualify, the item is discarded,
        // or a cost is below one.
        public TradeEntry? GetOffer(LootContext context)
        {
            if (this.MerchantPredicate is not null && !this.MerchantPredicate.Test(context))
                return null;

            var stack = new ItemStack(this.Gives, this.GivesCount, this.GivesComponents);
            if (this.GivenItemModifier is not null)
            {
                stack = this.GivenItemModifier.Apply(stack, context);
                if (stack.IsAir || stack.Count <= 0)
                    return null;
            }

            var additionalCost = TakeAdditionalCost(stack);
            if (this.StoresDoublePriceEnchantment(stack))
                additionalCost *= 2;

            var wants = this.Wants.ToItemCost(context, additionalCost);
            if (wants.Count < 1)
                return null;

            var additionalWants = this.AdditionalWants?.ToItemCost(context, 0);
            if (additionalWants is { Count: < 1 })
                return null;

            return new TradeEntry
            {
                FirstInput = wants,
                SecondInput = additionalWants ?? new TradeItem(ItemsRegistry.Air.Id, 0, []),
                Output = stack,
                MaxCount = Math.Max(1, this.MaxUses.GetInt(context.Numbers)),
                XP = Math.Max(0, this.Xp.GetInt(context.Numbers)),
                Multiplier = Math.Max(0f, this.ReputationDiscount.GetFloat(context.Numbers))
            };
        }

        // The additional_trade_cost component enchanting modifiers set: part of the price, never of the item sold.
        private static int TakeAdditionalCost(ItemStack stack)
        {
            var cost = stack.GetComponent<SimpleDataComponent<int>>(DataComponentType.AdditionalTradeCost)?.Value;
            if (cost is null)
                return 0;

            // No item has the component by default, so it leaves the patch rather than being marked removed.
            stack.Remove(DataComponentType.AdditionalTradeCost);
            stack.RemoveComponents.Remove(DataComponentType.AdditionalTradeCost);
            return cost.Value;
        }

        private bool StoresDoublePriceEnchantment(ItemStack stack)
        {
            if (this.DoubleTradePriceEnchantments is null)
                return false;

            var stored = stack.GetComponent<SimpleDataComponent<ImmutableArray<Enchantment>>>(
                DataComponentType.StoredEnchantments)?.Value ?? [];
            return stored.Any(enchantment =>
                this.DoubleTradePriceEnchantments.Contains(EnchantmentsRegistry.All[enchantment.Id].Identifier));
        }
    }

    // Item components of a cost or result. Vanilla's trades only set a potion (the water bottle the wandering trader
    // buys), so that's the one component read.
    private static ImmutableArray<DataComponent> ReadComponents(JsonElement json)
    {
        if (!json.TryGetProperty("components", out var components))
            return [];

        var read = ImmutableArray.CreateBuilder<DataComponent>();
        foreach (var component in components.EnumerateObject())
        {
            if (component.Name != "minecraft:potion_contents")
                throw new JsonException($"Trades can't set {component.Name} yet.");

            var potion = component.Value.GetProperty("potion").GetString()!;
            var parsed = Enum.Parse<Potion>(potion.Split(':')[^1].Replace("_", ""), true);
            read.Add(new PotionContentsDataComponent { Potion = parsed });
        }

        return read.ToImmutable();
    }
}
