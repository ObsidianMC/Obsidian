namespace Obsidian.API;
public interface INoise
{
    public string Type { get; }

    double GetValue(double x, double y, double z);
}
