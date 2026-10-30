using Obsidian.API.Inventory;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.Net;

namespace Obsidian.Entities;

/// <summary>Saves a merchant's offers and loads both Obsidian and vanilla saves.</summary>
/// <remarks>
/// Costs and results are saved in their network form, so their components (like the potion of a water bottle the
/// wandering trader buys) survive a reload. Offers saved before costs kept components
/// (<c>FirstId</c>/<c>FirstCount</c>) still load, without cost components.
/// </remarks>
internal static class MerchantOffers
{
    internal static void Write(INbtWriter writer, ImmutableArray<TradeEntry> offers)
    {
        writer.WriteListStart("ObsidianOffers", NbtTagType.Compound, offers.Length);
        foreach (var offer in offers)
        {
            writer.WriteCompoundStart();
            writer.WriteArray("FirstCost", CostBytes(offer.FirstInput));
            writer.WriteArray("SecondCost", CostBytes(offer.SecondInput));

            var output = new NetworkBuffer();
            output.WriteItemStack(offer.Output);
            writer.WriteArray("Output", output.AsSpan(0, output.Size));

            writer.WriteInt("Uses", offer.UsedCount);
            writer.WriteInt("MaxUses", offer.MaxCount);
            writer.WriteInt("Xp", offer.XP);
            writer.WriteByte("RewardExp", offer.RewardExp ? (byte)1 : (byte)0);
            writer.WriteInt("Demand", offer.Demand);
            writer.WriteInt("Discount", offer.Discount);
            writer.WriteFloat("Multiplier", offer.Multiplier);
            writer.EndCompound();
        }
        writer.EndList();
    }

    internal static ImmutableArray<TradeEntry> Read(NbtCompound tag)
    {
        if (!tag.TryGetTag<NbtList>("ObsidianOffers", out var saved))
            return ReadVanilla(tag);

        List<TradeEntry> offers = [];
        foreach (var entry in saved.OfType<NbtCompound>())
        {
            if (ReadOffer(entry) is TradeEntry offer)
                offers.Add(offer);
        }

        return offers.ToImmutableArray();
    }

    private static TradeEntry? ReadOffer(NbtCompound entry)
    {
        if (!entry.TryGetTag<NbtArray<byte>>("Output", out var bytes))
            return null;

        var output = new NetworkBuffer(bytes.GetArray()).ReadItemStack();
        if (output == null || output.IsAir || output.Count <= 0)
            return null;

        var first = ReadCost(entry, "First", minimum: 1);
        var second = ReadCost(entry, "Second", minimum: 0);
        if (first is null || second is null)
            return null;

        var multiplier = entry.TryGetTagValue<float>("Multiplier", out var saved) && float.IsFinite(saved)
            ? saved
            : 0.05f;
        var offer = new TradeEntry
        {
            FirstInput = first,
            SecondInput = second,
            Output = output,
            MaxCount = Math.Max(1, entry.GetInt("MaxUses")),
            UsedCount = Math.Max(0, entry.GetInt("Uses")),
            XP = Math.Max(0, entry.GetInt("Xp")),
            RewardExp = !entry.TryGetBool("RewardExp", out var reward) || reward,
            Demand = entry.GetInt("Demand"),
            Discount = entry.GetInt("Discount"),
            Multiplier = multiplier
        };
        offer.IsDisabled = offer.UsedCount >= offer.MaxCount;
        return offer;
    }

    private static ImmutableArray<TradeEntry> ReadVanilla(NbtCompound tag)
    {
        if (!tag.TryGetTag<NbtCompound>("Offers", out var saved)
            || !saved.TryGetTag<NbtList>("Recipes", out var recipes))
        {
            return [];
        }

        var offers = ImmutableArray.CreateBuilder<TradeEntry>();
        foreach (var recipe in recipes.OfType<NbtCompound>())
        {
            if (!recipe.TryGetTag<NbtCompound>("buy", out var buy)
                || buy.ItemFromNbt() is not ItemStack first
                || !recipe.TryGetTag<NbtCompound>("sell", out var sell)
                || sell.ItemFromNbt() is not ItemStack output)
            {
                continue;
            }

            var second = recipe.TryGetTag<NbtCompound>("buyB", out var buyB) ? buyB.ItemFromNbt() : null;
            // MerchantOffer.CODEC defaults: four uses, one villager XP, rewardExp true and no price adjustments.
            var offer = new TradeEntry
            {
                FirstInput = new TradeItem(first.Holder.Id, first.Count, [.. first.Patch]),
                SecondInput = second is null
                    ? new TradeItem(ItemsRegistry.Air.Id, 0, [])
                    : new TradeItem(second.Holder.Id, second.Count, [.. second.Patch]),
                Output = output,
                UsedCount = recipe.TryGetTagValue<int>("uses", out var uses) ? uses : 0,
                MaxCount = recipe.TryGetTagValue<int>("maxUses", out var maximum) ? maximum : 4,
                XP = recipe.TryGetTagValue<int>("xp", out var xp) ? xp : 1,
                RewardExp = !recipe.TryGetBool("rewardExp", out var reward) || reward,
                Demand = recipe.TryGetTagValue<int>("demand", out var demand) ? demand : 0,
                Discount = recipe.TryGetTagValue<int>("specialPrice", out var specialPrice) ? specialPrice : 0,
                Multiplier = recipe.TryGetTagValue<float>("priceMultiplier", out var multiplier) ? multiplier : 0
            };
            offer.IsDisabled = offer.UsedCount >= offer.MaxCount;
            offers.Add(offer);
        }

        return offers.ToImmutable();
    }

    private static byte[] CostBytes(TradeItem cost)
    {
        var buffer = new NetworkBuffer();
        buffer.WriteItemCost(cost);
        return buffer.AsSpan(0, buffer.Size).ToArray();
    }

    // A saved cost (prefix "First" or "Second"), within its item's stack size; null for an unknown item.
    private static TradeItem? ReadCost(NbtCompound entry, string prefix, int minimum)
    {
        TradeItem cost;
        if (entry.TryGetTag<NbtArray<byte>>(prefix + "Cost", out var bytes))
            cost = new NetworkBuffer(bytes.GetArray()).ReadItemCost();
        else
            cost = new TradeItem(entry.GetInt(prefix + "Id"), entry.GetInt(prefix + "Count"), []);

        if ((uint)cost.ID >= ItemsRegistry.All.Count)
            return null;

        var maximum = Math.Max(minimum, ItemsRegistry.Get(cost.ID).MaxStackSize);
        cost.Count = Math.Clamp(cost.Count, minimum, maximum);
        return cost;
    }
}
