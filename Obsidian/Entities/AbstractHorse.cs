using Obsidian.Net;

namespace Obsidian.Entities;

public class AbstractHorse : Animal
{
    public HorseMask HorseMask { get; set; }

    public Guid Owner { get; set; }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(16, EntityMetadataType.Byte);
        writer.WriteByte((byte)HorseMask);

        writer.WriteEntityMetadataType(17, EntityMetadataType.OptionalLivingEntityReference);
        writer.WriteBoolean(true);
        if (true)
            writer.WriteUuid(Owner);
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
