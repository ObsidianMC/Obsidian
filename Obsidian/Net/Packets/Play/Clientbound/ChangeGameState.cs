using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public abstract partial class ChangeGameState<T> : IClientboundPacket
    {
        [Field(0), ActualType(typeof(byte))]
        public ChangeGameStateReason Reason { get; }

        [Field(1), ActualType(typeof(float))]
        public abstract T Value { get; set; }

        public int Id => 0x1D;

        public ChangeGameState()
        {
        }

        public ChangeGameState(ChangeGameStateReason reason)
        {
            Reason = reason;
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
