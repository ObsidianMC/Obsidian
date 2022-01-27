using Obsidian.Net;

namespace Obsidian.Entities;

public class Pig : Animal
{
    public bool HasSaddle { get; set; }

    public int TotalTimeBoost { get; set; }

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteEntityMetdata(16, EntityMetadataType.Boolean, HasSaddle);
        await stream.WriteEntityMetdata(17, EntityMetadataType.VarInt, TotalTimeBoost);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteEntityMetadataType(16, EntityMetadataType.Boolean);
        stream.WriteBoolean(HasSaddle);

        stream.WriteEntityMetadataType(17, EntityMetadataType.VarInt);
        stream.WriteVarInt(TotalTimeBoost);
    }
}
