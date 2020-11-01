namespace Obsidian.API.Events
{
    public class PlayerEventArgs : BaseMinecraftEventArgs
    {
        /// <summary>
        /// The player involved in this event
        /// </summary>
        public IPlayer Player { get; }

        protected PlayerEventArgs(IPlayer player) : base(player.Server)
        {
            this.Player = player;
        }
    }
}
