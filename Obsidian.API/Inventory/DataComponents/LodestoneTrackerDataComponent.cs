namespace Obsidian.API.Inventory.DataComponents;

/// <summary>
/// The lodestone a compass points to (<c>minecraft:lodestone_tracker</c>).
/// </summary>
public sealed record class LodestoneTrackerDataComponent : DataComponent
{
    /// <inheritdoc/>
    public override DataComponentType Type => DataComponentType.LodestoneTracker;

    /// <inheritdoc/>
    public override string Identifier => "minecraft:lodestone_tracker";

    private GlobalPos? target;

    /// <summary>
    /// Gets the lodestone's dimension and position, or null when the compass spins.
    /// </summary>
    public GlobalPos? Target
    {
        get => this.target;
        init => this.target = value;
    }

    private bool tracked;

    /// <summary>
    /// Gets whether the compass stops tracking once the lodestone is gone.
    /// </summary>
    public bool Tracked
    {
        get => this.tracked;
        init => this.tracked = value;
    }

    /// <inheritdoc/>
    protected override void ReadCore(INetStreamReader reader)
    {
        this.target = reader.ReadOptional<GlobalPos>();
        this.tracked = reader.ReadBoolean();
    }

    /// <inheritdoc/>
    public override void Write(INetStreamWriter writer)
    {
        writer.WriteOptional(this.Target);
        writer.WriteBoolean(this.Tracked);
    }
}
