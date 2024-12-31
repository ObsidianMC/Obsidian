namespace Obsidian.API.Inventory.DataComponents;
public sealed record class LodestoneTrackerDataComponent : IDataComponent
{
    public DataComponentType Type => DataComponentType.LodestoneTracker;

    public string Identifier => "minecraft:lodestone_tracker";

    public Vector? Target { get; set; }

    public bool Tracked { get; set; }

    public void Read(INetStreamReader reader)
    {
        this.Target = reader.ReadOptional<Vector>();
        this.Tracked = reader.ReadBoolean();
    }

    public void Write(INetStreamWriter writer)
    {
        writer.WriteOptional(this.Target);
        writer.WriteBoolean(this.Tracked);
    }
}
