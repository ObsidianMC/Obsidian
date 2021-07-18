using SharpNoise.Modules;
using System;

namespace Obsidian.API.Noise
{
    public class BitShiftInput : Module
    {
        public Module Source0 { get; set; }

        public int Amount { get; set; }

        public bool Left { get; set; }

        public BitShiftInput() : base(1)
        {

        }

        public override double GetValue(double x, double y, double z)
        {
            x = Left ? (int) Math.Floor(x) << Amount : (int)Math.Floor(x) >> Amount;
            y = Left ? (int) Math.Floor(y) << Amount : (int)Math.Floor(y) >> Amount;
            z = Left ? (int) Math.Floor(z) << Amount : (int)Math.Floor(z) >> Amount;
            return Source0.GetValue(x, y, z);
        }
    }
}
