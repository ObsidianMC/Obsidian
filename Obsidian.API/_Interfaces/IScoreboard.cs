using System.Threading.Tasks;

namespace Obsidian.API
{
    public interface IScoreboard
    {
        public Task RemoveObjectiveAsync();

        public Task CreateOrUpdateObjectiveAsync(IChatMessage title, DisplayType displayType = DisplayType.Integer);

        public Task CreateOrUpdateObjectiveAsync(string title, DisplayType displayType = DisplayType.Integer);

        public Task CreateOrUpdateScoreAsync(string scoreName, string displayText, int value = 0);

        public IScore GetScore(string scoreName);
    }
}
