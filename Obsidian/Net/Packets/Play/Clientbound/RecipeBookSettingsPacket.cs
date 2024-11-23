using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class RecipeBookSettingsPacket
{
    [Field(1)]
    public bool CraftingeBookOpen { get; init; }

    [Field(2)]
    public bool CraftingBookFilterActive { get; init; }

    [Field(3)]
    public bool SmeltingBookOpen { get; init; }

    [Field(4)]
    public bool SmeltingBookFilterActive { get; init; }

    [Field(5)]
    public bool BlastFurnaceBookOpen { get; init; }

    [Field(6)]
    public bool BlastFurnaceBookFilterActive { get; init; }

    [Field(7)]
    public bool SmokerBookOpen { get; init; }

    [Field(8)]
    public bool SmokerBookFilterActive { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteBoolean(this.CraftingeBookOpen);
        writer.WriteBoolean(this.CraftingBookFilterActive);

        writer.WriteBoolean(this.SmeltingBookOpen);
        writer.WriteBoolean(this.SmeltingBookFilterActive);

        writer.WriteBoolean(this.BlastFurnaceBookOpen);
        writer.WriteBoolean(this.BlastFurnaceBookFilterActive);

        writer.WriteBoolean(this.SmokerBookOpen);
        writer.WriteBoolean(this.SmokerBookFilterActive);
    }
}
