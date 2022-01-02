using System.Text.Json.Serialization;

namespace Obsidian.Utilities.Registry;

public class BlockPropertiesExtraJson
{
    [JsonPropertyName("level")]
    public int[] Levels { get; set; }

    [JsonPropertyName("note")]
    public int[] Notes { get; set; }

    [JsonPropertyName("age")]
    public int[] Ages { get; set; }

    [JsonPropertyName("power")]
    public int[] PowerStates { get; set; }

    [JsonPropertyName("moisture")]
    public int[] MoistureStates { get; set; }

    [JsonPropertyName("rotation")]
    public int[] RotationStates { get; set; }

    [JsonPropertyName("layers")]
    public int[] Layers { get; set; }

    [JsonPropertyName("bites")]
    public int[] BiteStates { get; set; }

    [JsonPropertyName("delay")]
    public int[] DelayStates { get; set; }

    [JsonPropertyName("honey_level")]
    public int[] HoneyLevels { get; set; }

    [JsonPropertyName("axis")]
    public string[] Axis { get; set; }

    [JsonPropertyName("facing")]
    public string[] Faces { get; set; }

    [JsonPropertyName("instrument")]
    public string[] Instruments { get; set; }

    [JsonPropertyName("part")]
    public string[] Parts { get; set; }

    [JsonPropertyName("shape")]
    public string[] Shapes { get; set; }

    [JsonPropertyName("half")]
    public string[] HalfStates { get; set; }

    [JsonPropertyName("type")]
    public string[] Types { get; set; }

    [JsonPropertyName("hinge")]
    public string[] Hinges { get; set; }

    [JsonPropertyName("mode")]
    public string[] Mode { get; set; }

    [JsonPropertyName("attachment")]
    public string[] Attachments { get; set; }
}
