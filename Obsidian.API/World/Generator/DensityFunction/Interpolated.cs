using Obsidian.API._Interfaces;

namespace Obsidian.API.World.Generator.DensityFunction;
internal class Interpolated : IDensityFunction
{
    public string Type => "minecraft:interpolated";

    public required IDensityFunction argument;

    public int size_horizontal = 1;

    public int size_vertical = 1;

    public double GetValue(double x, double y, double z)
    {
        int hscale = size_horizontal * 2;
        int vscale = size_vertical * 2;
        double result = 0;
        for (int h = -hscale; h < hscale; h++)
            for (int v = -vscale; v < vscale; v++)
            {
                result += argument.GetValue(x + h, y, z + v);
            }
        return result / ((size_horizontal * 4) * (size_vertical * 4));
    }
}
