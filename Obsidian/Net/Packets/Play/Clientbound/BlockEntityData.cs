using Obsidian.API;
using Obsidian.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class BlockEntityData : IClientboundPacket
    {
        [Field(0)]
        public Vector Position;

        [Field(1), ActualType(typeof(byte))]
        BlockEntityActionType Action;

        [Field(2)]
        public Nbt.INbtTag NBTData;

        public int Id => 0x09;
    }

    // https://wiki.vg/Protocol#Block_Entity_Data
    public enum BlockEntityActionType : byte
    {
        SetSpawnerData = 1,
        SetCommandBlockText = 2,
        SetBeaconData = 3,
        SetMobHeadRotationSkin = 4,
        DeclareConduit = 5,
        SetBannerPatterns = 6,
        SetStructureTileData = 7,
        SetEndGatewayDestination = 8,
        SetSignText = 9,
        Unused = 10,
        DeclareBed = 11,
        SetJigsawBlockData = 12,
        SetCampfireItems = 13,
        SetBeehiveInfo = 14
    }
}
