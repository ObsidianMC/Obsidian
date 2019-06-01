namespace Obsidian.GameState
{
    public class DemoMessageReason : ChangeGameStateReason
    {
        public DemoMessageReason(DemoMessageAction action) : base(5) => this.Action = action;

        public DemoMessageAction Action { get; set; }

        public override float Value => (float)Action;
    }

    public enum DemoMessageAction : int
    {
        ShowDemoScreen = 0,
        TellMovementControls = 101,
        TellJumpControls = 102,
        TellInventoryControls = 103
    }
}
