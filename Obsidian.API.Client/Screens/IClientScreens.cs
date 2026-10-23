using Obsidian.API.Client.Razor;

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

    /// <summary>
    /// Opens a screen of the mod's built in Razor, like <see cref="Open(ModScreen)"/>: a new
    /// <typeparamref name="TComponent"/>, given <paramref name="parameters"/> and the screen's
    /// <see cref="ScreenContext"/>.
    /// </summary>
    /// <param name="parameters">The component's parameters by name, copied as it opens; null for none.</param>
    public void Open<TComponent>(IReadOnlyDictionary<string, object?>? parameters = null)
        where TComponent : ModComponent
    {
        var copied = parameters?.ToDictionary() ?? new Dictionary<string, object?>();
        this.Open(new ComponentScreen(typeof(TComponent), copied));
    }
}
