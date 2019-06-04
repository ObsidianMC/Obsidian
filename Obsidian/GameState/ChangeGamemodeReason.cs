using Obsidian.PlayerData;

namespace Obsidian.GameState
{
    public class ChangeGamemodeReason : ChangeGameStateReason
    {
        public ChangeGamemodeReason(Gamemode gamemode) : base(3) => this.Gamemode = gamemode;

        public Gamemode Gamemode { get; set; }

        public override float Value => (int)Gamemode;
    }
}