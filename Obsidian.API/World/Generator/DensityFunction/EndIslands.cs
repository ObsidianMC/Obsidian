using Obsidian.API._Interfaces;

namespace Obsidian.API.World.Generator.DensityFunction;
internal class EndIslands : IDensityFunction
{
    public string Type => "minecraft:end_islands";

    public double GetValue(double x, double y, double z) => 0.1f; //TODO: End Island Noise function
}
