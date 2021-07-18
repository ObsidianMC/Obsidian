using SharpNoise.Modules;

namespace Obsidian.API.Noise
{
    public class Polariaze : Module
    {
        public Module Source0 { get; set; }

        public Polariaze() : base(1)
        {

        }

        public override double GetValue(double x, double y, double z)
        {
            return Source0.GetValue(x, y, z) > 0 ? 1 : -1;
        }
    }
}
