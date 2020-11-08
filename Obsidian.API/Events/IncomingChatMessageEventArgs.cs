namespace Obsidian.API.Events
{
    public class IncomingChatMessageEventArgs : PlayerEventArgs, ICancellable
    {
        /// <summary>
        /// The message that was sent.
        /// </summary>
        public string Message { get; }

        public bool Cancel { get; set; }

        public IncomingChatMessageEventArgs(IPlayer player, string message) : base(player)
        {
            this.Message = message;
        }
    }
}
