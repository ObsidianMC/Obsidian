namespace Obsidian.API;
public interface INoise : IRegistryResource
{
    public double MinValue { get; }
    public double MaxValue { get; }
    public void Create();
    double GetValue(double x, double y, double z);
}
