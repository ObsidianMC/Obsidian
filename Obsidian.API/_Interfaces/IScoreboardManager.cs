namespace Obsidian.API;

/// <summary>
/// Creates the server's scoreboards.
/// </summary>
public interface IScoreboardManager
{
    /// <summary>
    /// Gets the scoreboard named <c>default</c>, created with the server.
    /// </summary>
    public IScoreboard DefaultScoreboard { get; }

    /// <summary>
    /// Creates a scoreboard. A name already in use is logged as a warning, but the scoreboard is still created.
    /// </summary>
    /// <param name="name">The scoreboard's name, which is also its objective's name.</param>
    /// <returns>The new scoreboard.</returns>
    /// <remarks>
    /// Scoreboards created through a plugin's <see cref="IServer"/> are the plugin's, and are removed when it unloads.
    /// </remarks>
    public IScoreboard CreateScoreboard(string name);
}
