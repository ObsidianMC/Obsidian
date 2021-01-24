namespace Obsidian.API
{
    public interface IScoreboardManager
    {
        public IScoreboard DefaultScoreboard { get; }

        public IScoreboard CreateScoreboard(string name);
    }
}
