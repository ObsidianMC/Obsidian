using System.Timers;

namespace Obsidian.Entities
{
    public abstract class Entity
    {
        public readonly Timer TickTimer = new Timer();

        public Entity() { }

        public bool OnFire { get; private set; }
        public bool Sprinting { get; private set; }
        public bool Swimming { get; private set; }
        public bool Invisible { get; set; }
        public bool Glowing { get; private set; }

        /// <summary>
        /// true if the entity is flying with an elytra
        /// </summary>
        public bool Flying { get; private set; }

        public string CustomName { get; private set; }

        public bool CustomNameVisible { get; private set; }
        public bool Silent { get; private set; }
        public bool NoGravity { get; set; }
    }
}
