using Obsidian.API;

namespace Obsidian.Net.Packets.Play.Clientbound.GameState
{
    public class ChangeGamemodeState : ChangeGameState<Gamemode>
    {
        public override Gamemode Value { get; set; }

        public ChangeGamemodeState(Gamemode newMode) : base(ChangeGameStateReason.ChangeGamemode) => this.Value = newMode;
    }

}
