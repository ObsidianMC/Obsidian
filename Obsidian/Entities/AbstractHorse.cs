﻿using Obsidian.Net;

namespace Obsidian.Entities;

public class AbstractHorse : Animal
{
    public HorseMask HorseMask { get; set; }

    public Guid Owner { get; set; }

    public async override Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteEntityMetdata(16, EntityMetadataType.Byte, this.HorseMask);

        if (this.Owner != default)
            await stream.WriteEntityMetdata(17, EntityMetadataType.OptUuid, Owner, true);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteEntityMetadataType(16, EntityMetadataType.Byte);
        stream.WriteUnsignedByte((byte)HorseMask);

        stream.WriteEntityMetadataType(17, EntityMetadataType.OptUuid);
        stream.WriteBoolean(true);
        if (true)
            stream.WriteUuid(Owner);
    }
}

public enum HorseMask
{
    None,

    Tamed = 0x02,
    Saddled = 0x04,
    HasBred = 0x08,
    Eating = 0x10,
    Rearing = 0x20,
    MouthOpen = 0x40
}
