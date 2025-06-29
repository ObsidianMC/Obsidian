namespace Obsidian.SourceGenerators.Registry.Models;
public readonly struct CleanedNoises(Dictionary<string, TypeInformation> worldgenProperties, Dictionary<string, string> staticDensityFunctions,
    Dictionary<string, string> noiseTypes, Dictionary<string, string> surfaceConditions)
{
    public Dictionary<string, TypeInformation> WorldgenProperties { get; } = worldgenProperties;
    public Dictionary<string, string> StaticDensityFunctions { get; } = staticDensityFunctions;
    public Dictionary<string, string> SurfaceConditionFunctions { get; } = surfaceConditions;
    public Dictionary<string, string> NoiseTypes { get; } = noiseTypes;
}
