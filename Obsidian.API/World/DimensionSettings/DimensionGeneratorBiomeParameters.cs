namespace Obsidian.API.World.DimensionSettings;

public sealed record class DimensionGeneratorBiomeParameters
{
    public DimensionGeneratorParameterValue Offset { get; set; }

    public DimensionGeneratorParameterValue Continentalness { get; set; }

    public DimensionGeneratorParameterValue Weirdness { get; set; }

    public DimensionGeneratorParameterValue Erosion { get; set; }

    public DimensionGeneratorParameterValue Depth { get; set; }

    public DimensionGeneratorParameterValue Humidity { get; set; }

    public DimensionGeneratorParameterValue Temperature { get; set; }
}

public sealed record class DimensionGeneratorParameterValue
{
    public float? Value { get; set; }

    public DimensionGeneratorValueRange? Range { get; set; }
}
