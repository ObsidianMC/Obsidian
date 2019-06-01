namespace Obsidian.GameState
{
    public abstract class ChangeGameStateReason
    {
        protected ChangeGameStateReason(byte reason) => this.Reason = reason;
        public abstract float Value { get; }
        public byte Reason { get; }
    }
}
