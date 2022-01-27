using Obsidian.Net;

namespace Obsidian.Entities;

public class AgeableMob : PathfinderMob
{
    public bool IsBaby { get; set; }

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteEntityMetdata(15, EntityMetadataType.Boolean, IsBaby);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteEntityMetadataType(15, EntityMetadataType.Boolean);
        stream.WriteBoolean(IsBaby);
    }
}
