using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.API
{
    public interface IScoreboard
    {
        public List<ITeam> Teams { get; }

        public Task RemoveObjectiveAsync();

        public Task CreateOrUpdateObjectiveAsync(ChatMessage title, DisplayType displayType = DisplayType.Integer);

        public Task CreateOrUpdateScoreAsync(string scoreName, string displayText, int? value = null);

        public Task<bool> RemoveScoreAsync(string scoreName);

        public Task<ITeam> CreateTeamAsync(string name, ChatMessage displayName, NameTagVisibility nameTagVisibility,
            CollisionRule collisionRule, TeamColor color, params string[] entities);

        public Task<ITeam> CreateTeamAsync(string name, ChatMessage displayName, NameTagVisibility nameTagVisibility,
           CollisionRule collisionRule, TeamColor color, ChatMessage prefix, ChatMessage suffix, params string[] entities);

        public Score GetScore(string scoreName);
    }
}
