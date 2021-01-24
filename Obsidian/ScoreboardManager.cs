using Obsidian.API;

namespace Obsidian
{
    public class ScoreboardManager : IScoreboardManager
    {
        private Server server;

        public IScoreboard DefaultScoreboard { get; }

        public ScoreboardManager(Server server)
        {
            this.server = server;

            this.DefaultScoreboard = this.CreateScoreboard("default");
        }

        public IScoreboard CreateScoreboard(string name) => new Scoreboard(this.server)
        {
            Name = name
        };

    }
}
