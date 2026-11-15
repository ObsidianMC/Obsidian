namespace Obsidian.API;

/// <summary>
/// A scoreboard with one objective, its scores and its teams. Changes are queued for the players added with
/// <see cref="AddPlayer"/>; show it to a player with <see cref="IPlayer.DisplayScoreboardAsync"/>.
/// </summary>
/// <remarks>
/// A plugin's scoreboards are removed from players' screens when it unloads, and changing one afterwards throws
/// <see cref="InvalidOperationException"/>.
/// </remarks>
public interface IScoreboard
{
    /// <summary>
    /// Gets the scoreboard's teams.
    /// </summary>
    public List<ITeam> Teams { get; }

    /// <summary>
    /// Removes the objective from the players shown the scoreboard. Does nothing without one.
    /// </summary>
    public void RemoveObjective();

    /// <summary>
    /// Creates the scoreboard's objective, or changes its title and number format if it has one.
    /// </summary>
    /// <param name="title">The title shown above the scores.</param>
    /// <param name="displayType">How the scores are shown.</param>
    public void CreateOrUpdateObjective(ChatMessage title, DisplayType displayType = DisplayType.Integer);

    /// <summary>
    /// Adds the line <paramref name="scoreName"/>, or changes its text and score. A new line goes below the existing
    /// ones.
    /// </summary>
    /// <param name="scoreName">The key the line is found by.</param>
    /// <param name="displayText">The text shown for the line.</param>
    /// <param name="value">The line's new score, or <see langword="null"/> to keep it. A new line ignores it unless it is the first.</param>
    public void CreateOrUpdateScore(string scoreName, string displayText, int? value = null);

    /// <summary>
    /// Removes the line <paramref name="scoreName"/>.
    /// </summary>
    /// <param name="scoreName">The key the line is found by.</param>
    /// <returns><see langword="true"/> if the line was removed; otherwise, <see langword="false"/>.</returns>
    public bool RemoveScore(string scoreName);

    /// <summary>
    /// Creates a team on this scoreboard and sends it to every online player.
    /// </summary>
    /// <param name="name">The team's unique name.</param>
    /// <param name="displayName">The name shown for the team.</param>
    /// <param name="nameTagVisibility">Whose name tags the team's members see.</param>
    /// <param name="collisionRule">Which entities the team's members push.</param>
    /// <param name="color">The color of the team's members' names.</param>
    /// <param name="entities">The team's members: players' usernames, or other entities' UUIDs.</param>
    /// <returns>The new team.</returns>
    public ITeam CreateTeam(string name, ChatMessage displayName, NameTagVisibility nameTagVisibility,
        CollisionRule collisionRule, TeamColor color, params string[] entities);

    /// <summary>
    /// Creates a team on this scoreboard, with text shown before and after its members' names, and sends it to
    /// every online player.
    /// </summary>
    /// <param name="name">The team's unique name.</param>
    /// <param name="displayName">The name shown for the team.</param>
    /// <param name="nameTagVisibility">Whose name tags the team's members see.</param>
    /// <param name="collisionRule">Which entities the team's members push.</param>
    /// <param name="color">The color of the team's members' names.</param>
    /// <param name="prefix">The text shown before members' names.</param>
    /// <param name="suffix">The text shown after members' names.</param>
    /// <param name="entities">The team's members: players' usernames, or other entities' UUIDs.</param>
    /// <returns>The new team.</returns>
    public ITeam CreateTeam(string name, ChatMessage displayName, NameTagVisibility nameTagVisibility,
       CollisionRule collisionRule, TeamColor color, ChatMessage prefix, ChatMessage suffix, params string[] entities);

    /// <summary>
    /// Gets the line <paramref name="scoreName"/>.
    /// </summary>
    /// <param name="scoreName">The key the line is found by.</param>
    /// <returns>The line's score, or <see langword="null"/> if there is none.</returns>
    public Score GetScore(string scoreName);

    /// <summary>
    /// Adds the player with entity id <paramref name="entityId"/> to the players the scoreboard's changes are sent to.
    /// </summary>
    /// <param name="entityId">The player's entity id.</param>
    public void AddPlayer(int entityId);
    /// <summary>
    /// Stops sending the scoreboard's changes to the player with entity id <paramref name="entityId"/>.
    /// </summary>
    /// <param name="entityId">The player's entity id.</param>
    /// <returns><see langword="true"/> if the player was removed; otherwise, <see langword="false"/>.</returns>
    public bool RemovePlayer(int entityId);
}
