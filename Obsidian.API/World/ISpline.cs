namespace Obsidian.API.World;
public interface ISpline
{
    public double MinValue { get; }
    public double MaxValue { get; }

    public double Apply(double x, double y, double z);
}
