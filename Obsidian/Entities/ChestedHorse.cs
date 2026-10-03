namespace Obsidian.Entities;

public class ChestedHorse : AbstractHorse
{
    public bool HasChest { get; set; }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(18, EntityMetadataType.Boolean);
        writer.WriteBoolean(HasChest);
    }
}

public class Llama : ChestedHorse
{
    private static readonly string[] carpetColors = ["white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray",
        "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black"];
    public int Strength { get; set; }

    public int CarpetColor
    {
        get => Array.FindIndex(carpetColors, color => GetEquipment(Obsidian.API.Inventory.EquipmentSlot.Body)
            .Holder.UnlocalizedName == $"minecraft:{color}_carpet");
        set
        {
            if (value is < -1 or > 15)
                throw new ArgumentOutOfRangeException(nameof(value));
            SetEquipment(Obsidian.API.Inventory.EquipmentSlot.Body, value == -1 ? Obsidian.API.Inventory.ItemStack.Air :
                ItemsRegistry.GetSingleItem($"minecraft:{carpetColors[value]}_carpet"));
        }
    }

    public LlamaVariant Variant { get; set; }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(19, EntityMetadataType.VarInt);
        writer.WriteVarInt(Strength);

        writer.WriteEntityMetadataType(20, EntityMetadataType.VarInt);
        writer.WriteVarInt(Variant);
    }
}

public enum LlamaVariant : int
{
    CreamyLlama,

    WhiteLlama,

    BrownLlama,

    GrayLlama
}

public class Donkey : ChestedHorse { }
