using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class UpdateRecipeBookPacket : IClientboundPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public UnlockRecipeAction Action { get; init; }

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

    [Field(9)]
    public List<string> FirstRecipeIds { get; init; }

    [Field(10), Condition("Action == UnlockRecipeAction.Init")]
    public List<string> SecondRecipeIds { get; init; }

    public int Id => 0x41;
}

public enum UnlockRecipeAction : int
{
    Init,
    Add,
    Remove
}
