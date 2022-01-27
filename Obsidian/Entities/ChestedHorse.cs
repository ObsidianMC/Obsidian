using Obsidian.Net;

namespace Obsidian.Entities;

public class ChestedHorse : AbstractHorse
{
    public bool HasChest { get; set; }

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteEntityMetdata(18, EntityMetadataType.Boolean, HasChest);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteEntityMetadataType(18, EntityMetadataType.Boolean);
        stream.WriteBoolean(HasChest);
    }
}

public class Llama : ChestedHorse
{
    public int Strength { get; set; }

    public int CarpetColor { get; set; }

    public LlamaVariant Variant { get; set; }

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteEntityMetdata(19, EntityMetadataType.VarInt, Strength);
        await stream.WriteEntityMetdata(20, EntityMetadataType.VarInt, CarpetColor);
        await stream.WriteEntityMetdata(21, EntityMetadataType.VarInt, Variant);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteEntityMetadataType(19, EntityMetadataType.VarInt);
        stream.WriteVarInt(Strength);

        stream.WriteEntityMetadataType(20, EntityMetadataType.VarInt);
        stream.WriteVarInt(CarpetColor);

        stream.WriteEntityMetadataType(21, EntityMetadataType.VarInt);
        stream.WriteVarInt(Variant);
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
