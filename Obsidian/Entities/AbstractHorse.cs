using Obsidian.Net;
using System;
using System.Threading.Tasks;

namespace Obsidian.Entities
{
    public class AbstractHorse : Animal
    {
        public HorseMask HorseMask { get; set; }

        public Guid Owner { get; set; }

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await base.WriteAsync(stream);

            await stream.WriteEntityMetdata(16, EntityMetadataType.Byte, this.HorseMask);

            if(this.Owner != null)
                await stream.WriteEntityMetdata(17, EntityMetadataType.OptUuid, Owner, true);
        }
    }

    public class ZombieHorse : AbstractHorse { }
    public class SkeletonHorse : AbstractHorse { }

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
}
