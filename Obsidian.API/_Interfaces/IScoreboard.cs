using System.Threading.Tasks;

namespace Obsidian.API
{
    public interface IScoreboard
    {
        public Task RemoveObjectiveAsync();

        public Task CreateOrUpdateObjectiveAsync(ChatMessage title, DisplayType displayType = DisplayType.Integer);

        public Task CreateOrUpdateObjectiveAsync(string title, DisplayType displayType = DisplayType.Integer);

        public Task CreateOrUpdateScoreAsync(string scoreName, string displayText, int value = 0);

        public Task RemoveScoreAsync(string scoreName);

        public Score GetScore(string scoreName);
    }
}
