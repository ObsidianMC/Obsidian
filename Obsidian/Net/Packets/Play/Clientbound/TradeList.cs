using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class TradeListPacket : IClientboundPacket
{
    public TradeListPacket(int windowId, sbyte size, List<Trade> trades, EVillagerLevel villagerLevel, int experience, bool isRegularVillager, bool canRestock)
    {
        WindowId = windowId;
        Size = size;
        Trades = trades;
        VillagerLevel = villagerLevel;
        Experience = experience;
        IsRegularVillager = isRegularVillager;
        CanRestock = canRestock;
    }

    [Field(0), VarLength]
    public int WindowId { get; init; }

    [Field(1)]
    public sbyte Size { get; init; }

    [Field(2)]
    public List<Trade> Trades { get; init; }

    [Field(3), ActualType(typeof(int)), VarLength]
    public EVillagerLevel VillagerLevel { get; init; }

    [Field(4), VarLength]
    public int Experience { get; init; }
    
    [Field(5)]
    public bool IsRegularVillager { get; init; }
    
    [Field(6)]
    public bool CanRestock { get; init; }


    public int Id => 0x28;

}

public class Trade
{
    public Trade()
    {
    }

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

public enum EVillagerLevel
{
    Novice = 1,
    Apprentice = 2,
    Journeyman = 3,
    Expert = 4,
    Master = 5
}
