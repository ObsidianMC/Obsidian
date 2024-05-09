namespace Obsidian.API;
public interface INoise : IRegistryResource
{
    double GetValue(double x, double y, double z);
}
