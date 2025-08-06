namespace Obsidian.API.Inventory.DataComponents;
public sealed record class LodestoneTrackerDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.LodestoneTracker;

    public override string Identifier => "minecraft:lodestone_tracker";

    public GlobalPosition? Target { get; set; }

    public bool Tracked { get; set; }

    public override void Read(INetStreamReader reader)
    {
        this.Target = reader.ReadOptional<GlobalPosition>();
        this.Tracked = reader.ReadBoolean();
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteOptional(this.Target);
        writer.WriteBoolean(this.Tracked);
    }
}
