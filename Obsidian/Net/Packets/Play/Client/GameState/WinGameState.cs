namespace Obsidian.Net.Packets.Play.Client.GameState
{
    public class WinGameState : ChangeGameState<WinStateReason>
    {
        public override WinStateReason Value { get; set; }

        public WinGameState(WinStateReason newReason) => this.Value = newReason;
    }

    public enum WinStateReason
    {
        JustRespawnPlayer,

        RollCreditsAndRespawn
    }
}
