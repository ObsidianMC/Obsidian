namespace Obsidian.API._Interfaces;
public interface IDensityFunction
{
    public string Type { get; }

    double GetValue(double x, double y, double z);
}
