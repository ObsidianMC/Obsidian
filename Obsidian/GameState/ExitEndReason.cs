namespace Obsidian.GameState
{
    public class ExitEndReason : ChangeGameStateReason
    {
        public ExitEndReason(ExitEndAction action) : base(4) => this.Action = action;

        public ExitEndAction Action { get; set; }

        public override float Value => (float)Action;
    }

    public enum ExitEndAction
    {
        /// <summary>
        /// Immediately send Client Status of respawn without showing end credits
        /// </summary>
        Respawn = 0,

        /// <summary>
        /// Show end credits and respawn at the end (or when esc is pressed)
        /// </summary>
        ShowCredits = 1,
    }
}
