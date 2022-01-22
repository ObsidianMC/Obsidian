namespace Obsidian.Net.Packets.Play.Clientbound;

public class Trade
{
    public Trade(ItemStack inputItem1, ItemStack outputItem, bool hasSecondItem, ItemStack inputItem2, bool tradeDisabled, int numberOfTradeUses, int maximumNumberOfTradeUses, int xp, int specialPrice, float priceMultiplier, int demand): base()
    {
        InputItem1 = inputItem1;
        OutputItem = outputItem;
        HasSecondItem = hasSecondItem;
        InputItem2 = inputItem2;
        TradeDisabled = tradeDisabled;
        NumberOfTradeUses = numberOfTradeUses;
        MaximumNumberOfTradeUses = maximumNumberOfTradeUses;
        Xp = xp;
        SpecialPrice = specialPrice;
        PriceMultiplier = priceMultiplier;
        Demand = demand;
    }

    public ItemStack InputItem1 { get; init; }

    public ItemStack OutputItem { get; init; }

    public bool HasSecondItem { get; init; }

    public ItemStack InputItem2 { get; init; }

    public bool TradeDisabled { get; init; }

    public int NumberOfTradeUses { get; init; }

    public int MaximumNumberOfTradeUses { get; init; }

    public int Xp { get; init; }

    public int SpecialPrice { get; init; }

    public float PriceMultiplier { get; init; }

    public int Demand { get; init; }

}
