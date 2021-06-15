using Obsidian.API;
using Obsidian.Chat;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class ScoreboardObjectivePacket : IClientboundPacket
    {
        [Field(0)]
        public string ObjectiveName { get; set; }

        [Field(1), ActualType(typeof(sbyte))]
        public ScoreboardMode Mode { get; set; }

        [Field(2), ActualType(typeof(ChatMessage)), Condition(nameof(ShouldWriteValue))]
        public IChatMessage Value { get; set; }

        [Field(3), VarLength, ActualType(typeof(int)), Condition(nameof(ShouldWriteValue))]
        public DisplayType Type { get; set; }

        public int Id => 0x4A;

        private bool ShouldWriteValue => Mode is ScoreboardMode.Create or ScoreboardMode.Update;
    }

    public enum ScoreboardMode : sbyte
    {
        Create,

        Remove,

        Update
    }
}
