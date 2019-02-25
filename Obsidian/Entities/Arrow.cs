namespace Obsidian.Entities
{
    public class Arrow : Entity
    {
        public bool Crit { get; private set; }
        public bool NoClip { get; private set; }

        public string Uuid { get; private set; }
    }
}
