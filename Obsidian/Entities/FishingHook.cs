namespace Obsidian.Entities
{
    public class FishingHook : Entity
    {
        /// <summary>
        ///  Hooked entity id + 1, or 0 if there is no hooked entity 
        /// </summary>
        public int EntityId { get; private set; }
    }
}
