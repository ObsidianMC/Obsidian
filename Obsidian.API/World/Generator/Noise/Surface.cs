using Obsidian.API._Interfaces;

namespace Obsidian.API.World.Generator.Noise;
internal class Surface : INoise
{
    public string Type => "minecraft:surface";

    public required List<double> amplitudes;

    public required double firstOcatave;

    public double GetValue(double x, double y, double z) => throw new NotImplementedException();
}
