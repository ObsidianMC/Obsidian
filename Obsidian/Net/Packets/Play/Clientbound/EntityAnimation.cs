using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class EntityAnimation : IClientboundPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; init; }

        [Field(1), ActualType(typeof(byte))]
        public EAnimation Animation { get; init; }

        public int Id => 0x05;
    }

    public enum EAnimation : byte
    {
        SwingMainArm,
        TakeDamage,
        LeaveBed,
        SwingOffhand,
        CriticalEffect,
        MagicalCriticalEffect
    }
}
