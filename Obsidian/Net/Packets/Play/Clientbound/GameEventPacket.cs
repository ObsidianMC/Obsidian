using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class GameEventPacket
{
    [Field(0), ActualType(typeof(byte))]
    public ChangeGameStateReason Reason { get; }

    [Field(1)]
    public float Value { get; }

    public GameEventPacket(ChangeGameStateReason reason)
    {
        Reason = reason;
    }

    public GameEventPacket(ChangeGameStateReason reason, float value)
    {
        Reason = reason;
        Value = value;
    }

    public GameEventPacket(Gamemode gamemode)
    {
        Reason = ChangeGameStateReason.ChangeGamemode;
        Value = (float)gamemode;
    }

    public GameEventPacket(WinStateReason winStateReason)
    {
        Reason = ChangeGameStateReason.WinGame;
        Value = (float)winStateReason;
    }

    public GameEventPacket(DemoEvent demoEvent)
    {
        Reason = ChangeGameStateReason.DemoEvent;
        Value = (float)demoEvent;
    }

    public GameEventPacket(RespawnReason respawnReason)
    {
        Reason = ChangeGameStateReason.EnableRespawnScreen;
        Value = (float)respawnReason;
    }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteByte((byte)this.Reason);
        writer.WriteSingle(this.Value);
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

    EnableRespawnScreen,

    LimitedCrafting,
    
    StartWaitingForLevelChunks,
}
