namespace Obsidian.Entities
{
    public class AreaEffectCloud
    {
        public float Radius { get; private set; }

        /// <summary>
        /// Color (only for mob spell particle) 
        /// </summary>
        public int Color { get; private set; }

        /// <summary>
        ///  Ignore radius and show effect as single point, not area 
        /// </summary>
        public bool SinglePoint { get; private set; }


        public object Effect { get; private set; } //TODO: make particle effect class.
    }
}
