using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;

namespace Obsidian.Net.Packets.Play.Client
{
    public abstract class ChangeGameState<T> : Packet
    {
        [Field(0, Type = DataType.UnsignedByte)]

        public ChangeGameStateReason Reason { get; set; }

        [Field(1, Type = DataType.Float)]
        public abstract T Value { get; set; }

        public ChangeGameState() : base(0x1F) { }

        public ChangeGameState(ChangeGameStateReason reason) : base(0x1F)
        {
            this.Reason = reason;
        }
    }
    public enum ChangeGameStateReason : byte
    {
        NoRespawnBlockAvailable,

        EndRaining,
        BeginRaining,

        ChangeGamemode,

        WinGame,

        DemoEvent,

        ArrowHitPlayer,

        RainLevelChange,
        ThunderLevelChange,

        PlayerPufferfishStingSound,

        PlayElderGuardianMobAppearance,

        EnableRespawnScreen
    }
}