namespace Obsidian.GameState
{
    public abstract class ChangeGameStateReason
    {
        public abstract float Value { get; }
        public byte Reason { get; }

        protected ChangeGameStateReason(byte reason) => this.Reason = reason;
    }
}
