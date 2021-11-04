namespace Obsidian.API
{
    public sealed class ScoreboardObjective
    {
        public ScoreboardObjective(string objectiveName, ChatMessage value, DisplayType displayType)
        {
            ObjectiveName = objectiveName;
            Value = value;
            DisplayType = displayType;
        }

        public string ObjectiveName { get; internal set; }

        public ChatMessage Value { get; internal set; }

        public DisplayType DisplayType { get; internal set; }
    }
}
