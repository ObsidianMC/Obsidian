using Obsidian.API.Inventory;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.Net;

namespace Obsidian.Entities;

internal static class MerchantOffers
{
    internal static TradeEntry Trade(Material input, int cost, Material output, int count = 1, int maxUses = 12, int xp = 1,
        Material second = Material.Air, int secondCount = 0) => new()
    {
        FirstInput = new TradeItem(ItemsRegistry.Get(input).Id, cost, []),
        SecondInput = new TradeItem(ItemsRegistry.Get(second).Id, secondCount, []),
        Output = new ItemStack(ItemsRegistry.Get(output), count), MaxCount = maxUses, XP = xp, Multiplier = 0.05f
    };

    internal static void Write(INbtWriter writer, ImmutableArray<TradeEntry> offers)
    {
        writer.WriteListStart("ObsidianOffers", NbtTagType.Compound, offers.Length);
        foreach (var offer in offers)
        {
            writer.WriteCompoundStart();
            writer.WriteInt("FirstId", offer.FirstInput.ID); writer.WriteInt("FirstCount", offer.FirstInput.Count);
            writer.WriteInt("SecondId", offer.SecondInput.ID); writer.WriteInt("SecondCount", offer.SecondInput.Count);
            var output = new NetworkBuffer(); output.WriteItemStack(offer.Output);
            writer.WriteArray("Output", output.AsSpan(0, output.Size));
            writer.WriteInt("Uses", offer.UsedCount); writer.WriteInt("MaxUses", offer.MaxCount);
            writer.WriteInt("Xp", offer.XP); writer.WriteInt("Demand", offer.Demand);
            writer.WriteInt("Discount", offer.Discount); writer.WriteFloat("Multiplier", offer.Multiplier);
            writer.EndCompound();
        }
        writer.EndList();
    }

    internal static ImmutableArray<TradeEntry> Read(NbtCompound tag)
    {
        if (!tag.TryGetTag<NbtList>("ObsidianOffers", out var saved)) return [];
        List<TradeEntry> offers = [];
        foreach (var entry in saved.OfType<NbtCompound>())
        {
            if (!entry.TryGetTag<NbtArray<byte>>("Output", out var bytes)) continue;
            var output = new NetworkBuffer(bytes.GetArray()).ReadItemStack();
            if (output == null || output.IsAir || output.Count <= 0) continue;
            var first = entry.GetInt("FirstId"); var second = entry.GetInt("SecondId");
            if (ItemsRegistry.Get(first).UnlocalizedName == null || ItemsRegistry.Get(second).UnlocalizedName == null) continue;
            var offer = new TradeEntry { FirstInput = new TradeItem(first, Math.Clamp(entry.GetInt("FirstCount"), 1, 64), []),
                SecondInput = new TradeItem(second, Math.Clamp(entry.GetInt("SecondCount"), 0, 64), []),
                Output = output, MaxCount = Math.Max(1, entry.GetInt("MaxUses")), UsedCount = Math.Max(0, entry.GetInt("Uses")),
                XP = Math.Max(0, entry.GetInt("Xp")), Demand = entry.GetInt("Demand"), Discount = entry.GetInt("Discount"),
                Multiplier = entry.TryGetTagValue<float>("Multiplier", out var multiplier) && float.IsFinite(multiplier) ? multiplier : 0.05f };
            offer.IsDisabled = offer.UsedCount >= offer.MaxCount;
            offers.Add(offer);
        }
        return offers.ToImmutableArray();
    }
}
