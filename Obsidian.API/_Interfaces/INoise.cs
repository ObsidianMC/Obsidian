namespace Obsidian.API._Interfaces;
internal interface INoise
{
    public string Type { get; }

    double GetValue(double x, double y, double z);
}
