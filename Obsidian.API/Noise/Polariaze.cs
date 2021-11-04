using SharpNoise.Modules;

namespace Obsidian.API.Noise
{
    public class Polariaze : Module
    {
        public Module Source0 { get; set; }

        public double Center { get; set; } = 0;

        public Polariaze(Module source0) : base(1)
        {
            Source0 = source0;
        }

        public override double GetValue(double x, double y, double z)
        {
            return Source0.GetValue(x, y, z) > Center ? 1 : -1;
        }
    }
}
