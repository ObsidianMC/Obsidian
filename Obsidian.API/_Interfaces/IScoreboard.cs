namespace Obsidian.API;

public interface IScoreboard
{
    public List<ITeam> Teams { get; }

    public void RemoveObjective();

    public void CreateOrUpdateObjective(ChatMessage title, DisplayType displayType = DisplayType.Integer);

    public void CreateOrUpdateScore(string scoreName, string displayText, int? value = null);

    public bool RemoveScore(string scoreName);

    public ITeam CreateTeam(string name, ChatMessage displayName, NameTagVisibility nameTagVisibility,
        CollisionRule collisionRule, TeamColor color, params string[] entities);

    public ITeam CreateTeam(string name, ChatMessage displayName, NameTagVisibility nameTagVisibility,
       CollisionRule collisionRule, TeamColor color, ChatMessage prefix, ChatMessage suffix, params string[] entities);

    public Score GetScore(string scoreName);

    public void AddPlayer(int entityId);
    public bool RemovePlayer(int entityId);
}
