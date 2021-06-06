using Newtonsoft.Json;

namespace Obsidian.Utilities.Registry
{
    public class BlockPropertiesExtraJson
    {
        [JsonProperty("level")]
        public int[] Levels { get; set; }

        [JsonProperty("note")]
        public int[] Notes { get; set; }

        [JsonProperty("age")]
        public int[] Ages { get; set; }

        [JsonProperty("power")]
        public int[] PowerStates { get; set; }

        [JsonProperty("moisture")]
        public int[] MoistureStates { get; set; }

        [JsonProperty("rotation")]
        public int[] RotationStates { get; set; }

        [JsonProperty("layers")]
        public int[] Layers { get; set; }

        [JsonProperty("bites")]
        public int[] BiteStates { get; set; }

        [JsonProperty("delay")]
        public int[] DelayStates { get; set; }

        [JsonProperty("honey_level")]
        public int[] HoneyLevels { get; set; }

        [JsonProperty("axis")]
        public string[] Axis { get; set; }

        [JsonProperty("facing")]
        public string[] Faces { get; set; }

        [JsonProperty("instrument")]
        public string[] Instruments { get; set; }

        [JsonProperty("part")]
        public string[] Parts { get; set; }

        [JsonProperty("shape")]
        public string[] Shapes { get; set; }

        [JsonProperty("half")]
        public string[] HalfStates { get; set; }

        [JsonProperty("type")]
        public string[] Types { get; set; }

        [JsonProperty("hinge")]
        public string[] Hinges { get; set; }

        [JsonProperty("mode")]
        public string[] Mode { get; set; }

        [JsonProperty("attachment")]
        public string[] Attachments { get; set; }
    }

}
