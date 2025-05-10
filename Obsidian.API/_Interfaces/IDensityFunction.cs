namespace Obsidian.API;
public interface IDensityFunction : IRegistryResource
{
    public double MinValue { get; }
    public double MaxValue { get; }
    public double GetValue(double x, double y, double z);
}
