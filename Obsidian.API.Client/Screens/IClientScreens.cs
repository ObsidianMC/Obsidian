namespace Obsidian.API.Client.Screens;

/// <summary>
/// Opens a mod's screens: <see cref="IClientRegistry.Screens"/>.
/// </summary>
public interface IClientScreens
{
    /// <summary>
    /// Opens a screen of the mod's over the game, or over the screen shown, which Escape goes back to. It opens on the
    /// game's thread, so from another (like a payload handler's) it opens shortly after.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The screen is a <see cref="ModMenuScreen"/>, which only opens with its menu.
    /// </exception>
    public void Open(ModScreen screen);
}
