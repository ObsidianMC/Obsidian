using SharpNoise.Modules;

namespace Obsidian.API.Noise;

public class Interpolate : Module
{
    public Module Source0 { get; set; }

    public Interpolate(Module source0) : base(1)
    {
        Source0 = source0;
    }

    public override double GetValue(double x, double y, double z)
    {
        // Bitshift the source module by 1 one so we can
        // "divide" each pixel into 4 quadrants
        var shifted = new BitShiftInput(this.Source0)
        {
            Amount = 1,
            Left = false
        };

        var center = shifted.GetValue(x, y, z);

        bool isRight = Math.Abs(x) % 2 != 0; // This is b/c C# doesn't modulo negatives correctly.
        bool isTop = Math.Abs(z) % 2 != 0;

        // If this is the top right, and above and to the right are different but match each other, time to smooth
        if (isTop && isRight)
        {
            var top = shifted.GetValue(x, y, z + 1);
            var right = shifted.GetValue(x + 1, y, z);
            return top != center && top == right ? top : center;
        }

        if (!isTop && isRight)
        {
            var bottom = shifted.GetValue(x, y, z - 1);
            var right = shifted.GetValue(x + 1, y, z);
            return bottom != center && bottom == right ? bottom : center;
        }

        if (!isTop && !isRight)
        {
            var bottom = shifted.GetValue(x, y, z - 1);
            var left = shifted.GetValue(x - 1, y, z);
            return bottom != center && bottom == left ? bottom : center;
        }

        if (isTop && !isRight)
        {
            var top = shifted.GetValue(x, y, z + 1);
            var left = shifted.GetValue(x - 1, y, z);
            return top != center && top == left ? top : center;
        }

        // okay compiler, sure...
        return center;
    }
}
