namespace Obsidian.API;
public interface IDensityFunction
{
    public string Type { get; }

    public double GetValue(double x, double y, double z);
}
