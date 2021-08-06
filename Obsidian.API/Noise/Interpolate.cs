using SharpNoise.Modules;
using System;

namespace Obsidian.API.Noise
{
    public class Interpolate : Module
    {
        public Module Source0 { get; set; }

        public Interpolate() : base(1)
        {

        }

        public override double GetValue(double x, double y, double z)
        {
            // Bitshift the source module by 1 one so we can
            // "divide" each pixel into 4 quadrants
            var shifted = new BitShiftInput
            {
                Amount = 1,
                Left = false,
                Source0 = this.Source0
            };

            var center = shifted.GetValue(x, y, z);
            // We can bail now if this is already positive
            //if (center > 0) { return center; }

            bool isRight = Math.Abs(x) % 2 != 0; // This is b/c C# doesn't modulo negatives correctly.
            bool isTop = Math.Abs(z) % 2 != 0;

            var top = shifted.GetValue(x, y, z + 1);
            var bottom = shifted.GetValue(x, y, z - 1);
            var right = shifted.GetValue(x + 1, y, z);
            var left = shifted.GetValue(x - 1, y, z);

            // If this is the top right, and above and to the right are positive, make this corner positive too.
            if (isTop && isRight && ((top > 0 && right > 0) || (top <= 0 && right <= 0))) { return center; }

            // Repeat for other corners
            if (isRight && !isTop && right > 0 && bottom > 0) { return 1; }
            if (!isTop && !isRight && bottom > 0 && left > 0) { return 1; }
            if (!isRight && isTop && left > 0 && top > 0) { return 1; }
            return center;
        }
    }
}
