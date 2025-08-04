using Obsidian.API.Registries;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory;

public readonly struct Item : INetworkSerializable<Item>, IEquatable<Item>
{
    public required string UnlocalizedName { get; init; }

    public required Material Type { get; init; }

    public required short Id { get; init; }

    [SetsRequiredMembers]
    public Item(int id, string unlocalizedName, Material type)
    {
        Id = (short)id;
        UnlocalizedName = unlocalizedName;
        Type = type;
    }

    [SetsRequiredMembers]
    public Item(Item item)
    {
        Id = item.Id;
        UnlocalizedName = item.UnlocalizedName;
        Type = item.Type;
    }

    public static void Write(Item value, INetStreamWriter writer) => writer.WriteVarInt(value.Id);
    public static Item Read(INetStreamReader reader) => ItemsRegistry.Get(reader);

    public override string ToString() => $"{{{this.UnlocalizedName}}}";

    public bool Equals(Item other) => other.UnlocalizedName == this.UnlocalizedName && other.Type == this.Type && other.Id == this.Id;

    public override bool Equals(object? obj) => Equals((Item)obj);

    public static bool operator ==(Item item1, Item item2) => item1.Equals(item2);

    public static bool operator !=(Item item1, Item item2) => !item1.Equals(item2);
}
