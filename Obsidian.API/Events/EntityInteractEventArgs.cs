namespace Obsidian.API.Events
{
    public class EntityInteractEventArgs : EntityEventArgs
    {
        /// <summary>
        /// The player who interacted with the entity.
        /// </summary>
        public IPlayer Player { get; }

        /// <summary>
        /// True if the player is sneaking when this event is triggered.
        /// </summary>
        public bool Sneaking { get; }

        public PositionF TargetPosition { get; internal set; }

        public Hand Hand { get; internal set; }
        
        public EntityInteractEventArgs(IPlayer player, IEntity entity, IServer server, bool sneaking = false) : base(entity, server)
        {
            this.Player = player;
            this.Sneaking = sneaking;
        }
    }
}
