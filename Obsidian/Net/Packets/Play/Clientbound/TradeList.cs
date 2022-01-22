using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class TradeList : IClientboundPacket
{
    public TradeList(int windowId, sbyte size, List<Trade> trades, VillagerLevel villagerLevel, int experience, bool isRegularVillager, bool canRestock)
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
    public VillagerLevel VillagerLevel { get; init; }

    [Field(4), VarLength]
    public int Experience { get; init; }
    
    [Field(5)]
    public bool IsRegularVillager { get; init; }
    
    [Field(6)]
    public bool CanRestock { get; init; }


    public int Id => 0x28;

}
