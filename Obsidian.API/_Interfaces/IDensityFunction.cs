namespace Obsidian.API;
public interface IDensityFunction : IRegistryResource
{
    public double GetValue(double x, double y, double z);
}
