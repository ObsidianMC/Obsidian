using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;

namespace Obsidian.Net.Packets.Play.Server
{
    public class EntityAction : Packet
    {
        [Field(0, Type = DataType.VarInt)]
        public int EntityId { get; set; }

        [Field(1, Type = DataType.VarInt)]
        public EAction Action { get; set; }

        [Field(2, Type = DataType.VarInt)]
        public int JumpBoost { get; set; }

        public EntityAction() : base(0x1C) { }
    }

    public enum EAction
    {
        StartSneaking,
        StopSneaking,

        LeaveBed,

        StartSprinting,
        StopSprinting,

        StartJumpWithHorse,
        StopJumpWithHorse,
        OpenHorseInventory,

        StartFlyingWithElytra
    }
}
