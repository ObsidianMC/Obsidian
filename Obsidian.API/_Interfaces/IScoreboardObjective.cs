namespace Obsidian.API
{
    public interface IScoreboardObjective
    {
        public string ObjectiveName { get; }

        public IChatMessage Value { get; }

        public DisplayType DisplayType { get; }
    }
}
