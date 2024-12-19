namespace Obsidian.API.Inventory.DataComponents;
public sealed class ConsumableDataComponent : IDataComponent
{
    public DataComponentType Type => DataComponentType.Consumable;

    public string Identifier => "minecraft:consumable";

    public required float ConsumeSeconds { get; init; }

    public required ItemAnimation Animation { get; init; }

    public required SoundEffect Sound { get; init; }

    public bool HasConsumeParticles { get; init; }  



    public void Read(INetStreamReader reader) => throw new NotImplementedException();
    public void Write(INetStreamWriter writer) => throw new NotImplementedException();
}

public enum ItemAnimation
{
    None,
    Eat,
    Drink,
    Block,
    Bow,
    Spear,
    Crossbow,
    Spyglass,
    TootHorn,
    Brush,
    Bundle
}
