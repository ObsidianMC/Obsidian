using Obsidian.API;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// The list of trades a villager is offering.
/// </summary>
public partial class MerchantOffersPacket
{
    /// <summary>
    /// The window ID of the villager's trade list.
    /// </summary>
    [Field(0), VarLength]
    public int WindowId { get; set; }

    /// <summary>
    /// The offered trades list.
    /// </summary>
    [Field(1)]
    public TradeEntry[] Offers { get; set; }

    /// <summary>
    /// The level of the villager. 1: Novice, 2: Apprentice, 3: Journeyman, 4: Expert, 5: Master.
    /// </summary>
    [Field(2), VarLength]
    public int VillagerLevel { get; set; }

    /// <summary>
    /// The total experience of the villager. Always 0 for wandering traders.
    /// </summary>
    [Field(3), VarLength]
    public int VillagerExperience { get; set; }

    /// <summary>
    /// True if the villager is a regular villager, false if it is a wandering trader.
    /// </summary>
    [Field(4)]
    public bool IsRegularVillager { get; set; }

    /// <summary>
    /// True if the villager can restock their inventory, false otherwise.
    /// </summary>
    [Field(5)]
    public bool CanRestock { get; set; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(WindowId);
        writer.WriteLengthPrefixedArray((o) => TradeEntry.Write(o, writer), Offers);
        writer.WriteVarInt(VillagerLevel);
        writer.WriteVarInt(VillagerExperience);
        writer.WriteBoolean(IsRegularVillager);
        writer.WriteBoolean(CanRestock);
    }
}
