namespace Obsidian.API.Inventory.DataComponents;

public sealed record class LodestoneTrackerDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.LodestoneTracker;

    public override string Identifier => "minecraft:lodestone_tracker";

    private GlobalPos? target;

    public GlobalPos? Target
    {
        get => this.target;
        init => this.target = value;
    }

    private bool tracked;

    public bool Tracked
    {
        get => this.tracked;
        init => this.tracked = value;
    }

    protected override void ReadCore(INetStreamReader reader)
    {
        this.target = reader.ReadOptional<GlobalPos>();
        this.tracked = reader.ReadBoolean();
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteOptional(this.Target);
        writer.WriteBoolean(this.Tracked);
    }
}
