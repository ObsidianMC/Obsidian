using Obsidian.API.Inventory;
using System.Collections.Immutable;

namespace Obsidian.Net;

public partial class NetworkBuffer
{
    /// <summary>Reads vanilla ItemCost: item ID, count, and exact component predicates (no removal count).</summary>
    public TradeItem ReadItemCost()
    {
        var id = this.ReadVarInt();
        var count = this.ReadVarInt();
        var components = ImmutableArray.CreateBuilder<DataComponent>(this.ReadComponentCount());
        while (components.Count < components.Capacity)
            components.Add(this.ReadDataComponent((DataComponentType)this.ReadVarInt()));

        return new(id, count, components.MoveToImmutable());
    }

    public void WriteItemCost(TradeItem value)
    {
        this.WriteVarInt(value.ID);
        this.WriteVarInt(value.Count);
        this.WriteVarInt(value.Components.Length);
        foreach (var component in value.Components)
        {
            this.WriteVarInt(component.Type);
            this.WriteDataComponent(component);
        }
    }

    /// <summary>Reads a MerchantOffers entry, including its optional second cost and required output stack.</summary>
    public TradeEntry ReadMerchantOffer() => new()
    {
        FirstInput = this.ReadItemCost(),
        Output = this.ReadRequiredItemStack(),
        SecondInput = this.ReadBoolean() ? this.ReadItemCost() : null,
        IsDisabled = this.ReadBoolean(),
        UsedCount = this.ReadInt(),
        MaxCount = this.ReadInt(),
        XP = this.ReadInt(),
        Discount = this.ReadInt(),
        Multiplier = this.ReadSingle(),
        Demand = this.ReadInt()
    };

    public void WriteMerchantOffer(TradeEntry value)
    {
        this.WriteItemCost(value.FirstInput);
        this.WriteRequiredItemStack(value.Output);
        this.WriteBoolean(value.SecondInput is not null);
        if (value.SecondInput is not null)
            this.WriteItemCost(value.SecondInput);

        this.WriteBoolean(value.IsDisabled);
        this.WriteInt(value.UsedCount);
        this.WriteInt(value.MaxCount);
        this.WriteInt(value.XP);
        this.WriteInt(value.Discount);
        this.WriteSingle(value.Multiplier);
        this.WriteInt(value.Demand);
    }
}
