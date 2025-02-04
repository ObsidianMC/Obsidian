using Obsidian.API.Inventory;

namespace Obsidian.API;

/// <summary>
/// Represents a trade entry offered by a villager or wandering trader.
/// </summary>
public sealed record TradeEntry
{
    /// <summary>
    /// The first input item of the trade. The required count of the first input is the "base price" of the trade.
    /// The final price = base + floor(base * <see cref="Multiplier"/> * <see cref="Demand"/>) + <see cref="Discount"/>.
    /// </summary>
    public TradeItem FirstInput { get; set; }

    /// <summary>
    /// The output item of the trade.
    /// </summary>
    public ItemStack Output { get; set; }

    /// <summary>
    /// The second input item of the trade. The required count of the second input is not affected by discount
    /// or demand.
    /// </summary>
    public TradeItem SecondInput { get; set; }

    /// <summary>
    /// Whether the trade is disabled.
    /// </summary>
    public bool IsDisabled { get; set; }
    
    /// <summary>
    /// Number of times the trade has been used.
    /// </summary>
    public int UsedCount { get; set; }

    /// <summary>
    /// The maximum number of times the trade can be used before it becomes disabled.
    /// </summary>
    public int MaxCount { get; set; }

    /// <summary>
    /// Number of XPs the villager will earn after each trade.
    /// </summary>
    public int XP { get; set; }

    /// <summary>
    /// Used in calculating the final price. Can be zero or negative.
    /// </summary>
    public int Discount { get; set; }

    /// <summary>
    /// Used in calculating the final price. Can be low (0.05) or high (0.2).
    /// </summary>
    public float Multiplier { get; set; }

    /// <summary>
    /// Used in calculating the final price. Negative values are treated as zero.
    /// </summary>
    public int Demand { get; set; }

    /// <summary>
    /// Writes the data to an <see cref="INetStreamWriter"/>.
    /// </summary>
    /// <param name="writer">The writer to write to.</param>
    public void Write(INetStreamWriter writer)
    {
        FirstInput.Write(writer);
        writer.WriteItemStack(Output);
        SecondInput.Write(writer);
        writer.WriteBoolean(IsDisabled);
        writer.WriteInt(UsedCount);
        writer.WriteInt(MaxCount);
        writer.WriteInt(XP);
        writer.WriteInt(Discount);
        writer.WriteFloat(Multiplier);
        writer.WriteInt(Demand);
    }
}
