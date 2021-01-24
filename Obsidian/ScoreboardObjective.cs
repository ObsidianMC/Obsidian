using Obsidian.API;

namespace Obsidian
{
    public class ScoreboardObjective : IScoreboardObjective
    {
        public string ObjectiveName { get; set; }

        public IChatMessage Value { get; set; }

        public DisplayType DisplayType { get; set; }
    }
}
