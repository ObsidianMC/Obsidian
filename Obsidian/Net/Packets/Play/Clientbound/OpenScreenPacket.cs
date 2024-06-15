using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class OpenScreenPacket : IClientboundPacket
{
    [Field(0), VarLength]
    public int WindowId { get; }

    [Field(1), ActualType(typeof(int)), VarLength]
    public WindowType Type { get; }

    [Field(2)]
    public ChatMessage Title { get; }

    public int Id => 0x33;

    public OpenScreenPacket(BaseContainer inventory, int windowId)
    {
        Title = inventory.Title;

        if (Enum.TryParse<WindowType>(inventory.Type.ToString(), true, out var type))
            Type = type;
        else if (Enum.TryParse($"generic9x{inventory.Size / 9}", true, out type))
            Type = type;

        WindowId = windowId;
    }

    public override string ToString() => $"{this.WindowId}:{this.Type}";
}

// Do not mess up the order this is how it's supposed to be ordered
public enum WindowType : int
{
    Generic9x1,
    Generic9x2,
    Generic9x3,
    Generic9x4,
    Generic9x5,
    Generic9x6,
    Generic3x3,

    Anvil,
    Beacon,
    BlastFurnace,
    BrewingStand,
    Crafting,
    Enchantment,
    Furnace,
    Grindstone,
    Hopper,
    Lectern,
    Loom,
    Merchant,
    ShulkerBox,
    LegacySmithing,
    Smithing,
    Smoker,
    CartographyTable,
    Stonecutter
}
