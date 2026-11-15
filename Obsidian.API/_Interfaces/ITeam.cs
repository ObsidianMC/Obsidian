namespace Obsidian.API;

/// <summary>
/// A scoreboard team. Changing a property doesn't send it; call <see cref="Update"/> afterwards.
/// </summary>
public interface ITeam
{
    /// <summary>
    /// Gets the team's unique name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets the name shown for the team.
    /// </summary>
    public ChatMessage DisplayName { get; set; }

    /// <summary>
    /// Gets or sets who can see the team's members' name tags.
    /// </summary>
    public NameTagVisibility NameTagVisibility { get; set; }

    /// <summary>
    /// Gets or sets which entities the team's members push.
    /// </summary>
    public CollisionRule CollisionRule { get; set; }

    /// <summary>
    /// Gets or sets the color of the team's members' names.
    /// </summary>
    public TeamColor Color { get; set; }

    /// <summary>
    /// Gets or sets the text shown before the team's members' names.
    /// </summary>
    public ChatMessage Prefix { get; set; }

    /// <summary>
    /// Gets or sets the text shown after the team's members' names.
    /// </summary>
    public ChatMessage Suffix { get; set; }

    /// <summary>
    /// Gets the team's members: players' usernames, or other entities' UUIDs. Use <see cref="AddEntities"/> and
    /// <see cref="RemoveEntities"/> to change them, which also tells clients.
    /// </summary>
    public HashSet<string> Entities { get; }

    /// <summary>
    /// Adds entities to the team.
    /// </summary>
    /// <param name="entities">Identifiers for the entities in this team. For players, this is their username; for other entities, it is their UUID.</param>
    /// <returns>Returns the amount of entities added to the team.</returns>
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
