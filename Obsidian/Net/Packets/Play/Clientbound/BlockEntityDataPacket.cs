using Obsidian.Nbt;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class BlockEntityDataPacket : IClientboundPacket
{
    [Field(0)]
    public Vector Position { get; init; }

    [Field(1), VarLength]
    private int Type { get; init; }

    [Field(2)]
    public INbtTag NBTData { get; init; }

    public int Id => 0x07;
}

//// https://wiki.vg/Protocol#Block_Entity_Data
//public enum BlockEntityActionType : int
//{
//    SetSpawnerData = 1,
//    SetCommandBlockText = 2,
//    SetBeaconData = 3,
//    SetMobHeadRotationSkin = 4,
//    DeclareConduit = 5,
//    SetBannerPatterns = 6,
//    SetStructureTileData = 7,
//    SetEndGatewayDestination = 8,
//    SetSignText = 9,
//    Unused = 10,
//    DeclareBed = 11,
//    SetJigsawBlockData = 12,
//    SetCampfireItems = 13,
//    SetBeehiveInfo = 14
//}
