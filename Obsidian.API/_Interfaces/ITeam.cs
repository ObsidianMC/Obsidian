namespace Obsidian.API;

public interface ITeam
{
    public string Name { get; }

    public ChatMessage DisplayName { get; set; }

    public NameTagVisibility NameTagVisibility { get; set; }

    public CollisionRule CollisionRule { get; set; }

    public TeamColor Color { get; set; }

    public ChatMessage Prefix { get; set; }

    public ChatMessage Suffix { get; set; }

    public HashSet<string> Entities { get; }

    /// <summary>
    /// Adds entities to the team.
    /// </summary>
    /// <param name="entities">Identifiers for the entities in this team. For players, this is their username; for other entities, it is their UUID.</param>
    /// /// <returns>Returns the amount of entities added to the team.</returns>
    public int AddEntities(params string[] entities);

    /// <summary>
    /// Removes entities to the team.
    /// </summary>
    /// <param name="entities">Identifiers for the entities in this team. For players, this is their username; for other entities, it is their UUID.</param>
    /// <returns>Returns the amount of entities removed from the team.</returns>
    public int RemoveEntities(params string[] entities);

    /// <summary>
    /// Updates the team for all the players on the server
    /// </summary>
    public void Update();

    /// <summary>
    /// Deletes the team
    /// </summary>
    public void Delete();
}
