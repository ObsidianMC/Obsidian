namespace Obsidian.API.Events
{
    public class EntityEventArgs : BaseMinecraftEventArgs, ICancellable
    {
        public IEntity Entity { get; }

        public bool Cancel { get; set; }

        public EntityEventArgs(IEntity entity, IServer server) : base(server)
        {
            this.Entity = entity;
        }
    }
}
