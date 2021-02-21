namespace Obsidian.API
{
    public sealed class ScoreboardObjective
    {
        public string ObjectiveName { get; internal set; }

        public IChatMessage Value { get; internal set; }

        public DisplayType DisplayType { get; internal set; }
    }
}
