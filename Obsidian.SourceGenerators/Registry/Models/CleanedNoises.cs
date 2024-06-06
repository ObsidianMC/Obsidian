namespace Obsidian.SourceGenerators.Registry.Models;
public readonly struct CleanedNoises
{
    public Dictionary<string, TypeInformation> DensityFunctionTypes { get; }
    public Dictionary<string, string> StaticDensityFunctions { get; }
    public Dictionary<string, string> NoiseTypes { get; }

    public CleanedNoises(Dictionary<string, TypeInformation> densityFunctionTypes, Dictionary<string, string> staticDensityFunctions,
        Dictionary<string, string> noiseTypes)
    {
        this.DensityFunctionTypes = densityFunctionTypes;
        this.StaticDensityFunctions = staticDensityFunctions;
        this.NoiseTypes = noiseTypes;
    }
}
