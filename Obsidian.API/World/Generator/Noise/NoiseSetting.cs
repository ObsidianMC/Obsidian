namespace Obsidian.API.World.Generator.Noise;
public sealed class NoiseSetting
{
    public bool AquifersEnabled { get; set; }

    public SimpleBlockState DefaultBlock { get; set; }

    public SimpleBlockState DefaultFluid { get; set; }

    public bool DisableMobGeneration { get; set; }

    public bool LegacyRandomSource { get; set; }

    public NoiseConfiguration Noise { get; set; }

    public NoiseRouter NoiseRouter { get; set; }

    public bool OreVeinsEnabled { get; set; }

    public int SeaLevel { get; set; }

    public ClimateParameter[] SpawnTarget { get; set; }

    public ISurfaceRule SurfaceRule { get; set; }
}
