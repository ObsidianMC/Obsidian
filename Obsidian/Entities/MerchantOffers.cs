using Obsidian.API.Inventory;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.Net;

namespace Obsidian.Entities;

/// <summary>Saves and loads a merchant's offers (Obsidian's own <c>ObsidianOffers</c> list).</summary>
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
            return [];

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
            Demand = entry.GetInt("Demand"),
            Discount = entry.GetInt("Discount"),
            Multiplier = multiplier
        };
        offer.IsDisabled = offer.UsedCount >= offer.MaxCount;
        return offer;
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
