using Obsidian.API.Inventory;

namespace Obsidian.API;

/// <summary>
/// Represents an item stack supplied as the "price" in a <see cref="TradeEntry"/>.
/// </summary>
public sealed record TradeItem : INetworkSerializable<TradeItem>
{
    /// <summary>
    /// The item ID.
    /// </summary>
    public int ID { get; set; }

    /// <summary>
    /// The item count.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// The item's components. An item stack must match all the component requirements to be considered a
    /// valid input.
    /// </summary>
    public DataComponent[] Components { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TradeItem"/> record.
    /// </summary>
    /// <param name="id">The item ID.</param>
    /// <param name="count">The item count.</param>
    /// <param name="components">The item's components.</param>
    public TradeItem(int id, int count, DataComponent[] components)
    {
        ID = id;
        Count = count;
        Components = components;
    }

    public static void Write(TradeItem value, INetStreamWriter writer)
    {
        writer.WriteVarInt(value.ID);
        writer.WriteVarInt(value.Count);
        writer.WriteLengthPrefixedArray((c) =>
        {
            writer.WriteVarInt(c.Type);
            c.Write(writer);
        }, value.Components);
    }

    // No need to implement
    public static TradeItem Read(INetStreamReader reader) => throw new NotImplementedException();
}
