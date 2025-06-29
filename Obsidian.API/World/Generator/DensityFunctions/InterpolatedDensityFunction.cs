namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:interpolated")]
public sealed class InterpolatedDensityFunction : IDensityFunction
{
    private const int sizeHorizontal = 1;
    private const int sizeVertical = 1;

    public string Type => "minecraft:interpolated";

    public required IDensityFunction Argument { get; init; }

    public double MinValue
    {
        get
        {
            int hscale = sizeHorizontal * 2;
            int vscale = sizeVertical * 2;
            int totalSamples = hscale * vscale * 4;
            return Argument.MinValue * totalSamples / ((sizeHorizontal * 4) * (sizeVertical * 4));
        }
    }

    public double MaxValue
    {
        get
        {
            int hscale = sizeHorizontal * 2;
            int vscale = sizeVertical * 2;
            int totalSamples = hscale * vscale * 4;
            return Argument.MaxValue * totalSamples / ((sizeHorizontal * 4) * (sizeVertical * 4));
        }
    }

    public double GetValue(double x, double y, double z)
    {
        int hscale = sizeHorizontal * 2;
        int vscale = sizeVertical * 2;
        double result = 0;
        for (int h = -hscale; h < hscale; h++)
            for (int v = -vscale; v < vscale; v++)
            {
                result += Argument.GetValue(x + h, y, z + v);
            }
        return result / ((sizeHorizontal * 4) * (sizeVertical * 4));
    }
}
