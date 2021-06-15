using Obsidian.API;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class ChangeGameState : IClientboundPacket
    {
        [Field(0), ActualType(typeof(byte))]
        public ChangeGameStateReason Reason { get; }

        [Field(1)]
        public float Value { get; }

        public int Id => 0x1D;

        public ChangeGameState(ChangeGameStateReason reason)
        {
            Reason = reason;
        }

        public ChangeGameState(Gamemode gamemode)
        {
            Reason = ChangeGameStateReason.ChangeGamemode;
            Value = (float)gamemode;
        }

        public ChangeGameState(WinStateReason winStateReason)
        {
            Reason = ChangeGameStateReason.WinGame;
            Value = (float)winStateReason;
        }

        public ChangeGameState(DemoEvent demoEvent)
        {
            Reason = ChangeGameStateReason.DemoEvent;
            Value = (float)demoEvent;
        }

        public ChangeGameState(RespawnReason respawnReason)
        {
            Reason = ChangeGameStateReason.EnableRespawnScreen;
            Value = (float)respawnReason;
        }
    }

    public enum WinStateReason
    {
        JustRespawnPlayer,
        RollCreditsAndRespawn
    }

    public enum RespawnReason
    {
        EnableRespawnScreen,
        ImmediatelyRespawn
    }

    public enum DemoEvent
    {
        ShowWelcomeScreen = 0,

        TellMovementControls = 101,
        TellJumpControl = 102,
        TellInventoryControl = 103,
        TellDemoOver = 104
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
