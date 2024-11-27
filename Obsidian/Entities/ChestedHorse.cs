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
    public int Strength { get; set; }

    public int CarpetColor { get; set; }

    public LlamaVariant Variant { get; set; }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(19, EntityMetadataType.VarInt);
        writer.WriteVarInt(Strength);

        writer.WriteEntityMetadataType(20, EntityMetadataType.VarInt);
        writer.WriteVarInt(CarpetColor);

        writer.WriteEntityMetadataType(21, EntityMetadataType.VarInt);
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
