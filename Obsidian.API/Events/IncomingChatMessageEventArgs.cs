namespace Obsidian.API.Events
{
    public class IncomingChatMessageEventArgs : PlayerEventArgs
    {
        /// <summary>
        /// The message that was sent.
        /// </summary>
        public string Message { get; }

        public IncomingChatMessageEventArgs(IPlayer player, string message) : base(player)
        {
            this.Message = message;
        }
    }
}
