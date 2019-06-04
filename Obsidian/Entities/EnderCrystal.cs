namespace Obsidian.Entities
{
    public class EnderCrystal : Entity
    {
        public Location BeamTarget { get; private set; }

        public bool ShowBottom { get; private set; } = true;
    }
}
