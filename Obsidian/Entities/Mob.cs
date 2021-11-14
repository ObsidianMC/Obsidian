using Obsidian.Net;
using System;
using System.Threading.Tasks;

namespace Obsidian.Entities;

public class Mob : Living
{
    public MobBitmask MobBitMask { get; set; } = MobBitmask.None;

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteEntityMetdata(14, EntityMetadataType.Byte, this.MobBitMask);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteEntityMetadataType(14, EntityMetadataType.Byte);
        stream.WriteByte((byte)MobBitMask);
    }
}

[Flags]
public enum MobBitmask
{
    None = 0x00,
    NoAi = 0x01,
    LeftHanded = 0x02,
    Agressive = 0x04
}
