using Obsidian.API._Interfaces;

namespace Obsidian.API.World.Generator.DensityFunction;
internal class RangeChoice : IDensityFunction
{
    public string Type => "minecraft:range_choice";

    public required IDensityFunction input;

    public required IDensityFunction when_in_range;

    public required IDensityFunction when_out_of_range;

    public required double min_inclusive;

    public required double max_exclusive;

    public double GetValue(double x, double y, double z)
    {
        var control = input.GetValue(x, y, z);
        if (control >= min_inclusive && control < max_exclusive)
        {
            return when_in_range.GetValue(x, y, z);
        }
        return when_out_of_range.GetValue(x, y, z);
    }
}
