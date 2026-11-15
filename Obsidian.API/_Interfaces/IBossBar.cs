using Obsidian.API.Boss;

namespace Obsidian.API;

/// <summary>
/// A boss bar shown at the top of the screen of the players added to it. Each change is sent to those players.
/// </summary>
/// <remarks>
/// A plugin's boss bars are hidden when it unloads, and changing one afterwards throws
/// <see cref="InvalidOperationException"/>.
/// </remarks>
public interface IBossBar
{
    /// <summary>
    /// Gets the id that identifies the bar to clients.
    /// </summary>
    public Guid Uuid { get; }

    /// <summary>
    /// Returns whether the bar is shown to the player with entity id <paramref name="id"/>.
    /// </summary>
    /// <param name="id">The player's entity id.</param>
    public bool HasPlayer(int id);

    /// <summary>
    /// Shows the bar to the player with entity id <paramref name="id"/>. Does nothing if it is already shown to them.
    /// </summary>
    /// <param name="id">The player's entity id.</param>
    public void AddPlayer(int id);

    /// <summary>
    /// Hides the bar from the player with entity id <paramref name="id"/>. Does nothing if it isn't shown to them.
    /// </summary>
    /// <param name="id">The player's entity id.</param>
    public void RemovePlayer(int id);

    /// <summary>
    /// Sets the text shown above the bar.
    /// </summary>
    /// <param name="newTitle">The new title.</param>
    public void UpdateTitle(ChatMessage newTitle);

    /// <summary>
    /// Sets how full the bar is, from 0 (empty) to 1 (full).
    /// </summary>
    /// <param name="newHealth">The new fill.</param>
    public void UpdateHealth(float newHealth);

    /// <summary>
    /// Sets the bar's color.
    /// </summary>
    /// <param name="newColor">The new color.</param>
    public void UpdateColor(BossBarColor newColor);

    /// <summary>
    /// Sets how many notches the bar is divided into.
    /// </summary>
    /// <param name="newDivision">The new division.</param>
    public void UpdateDivision(BossBarDivisionType newDivision);

    /// <summary>
    /// Turns on <paramref name="newFlags"/>, keeping the flags already set.
    /// </summary>
    /// <param name="newFlags">The flags to add.</param>
    public void AddFlags(BossBarFlags newFlags);

    /// <summary>
    /// Turns off <paramref name="flag"/>.
    /// </summary>
    /// <param name="flag">The flags to remove.</param>
    public void RemoveFlag(BossBarFlags flag);
}
