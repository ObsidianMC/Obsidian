namespace Obsidian.API.Client;

/// <summary>
/// A plugin with a client side: implemented by a mod's <see cref="Plugins.PluginBase"/> subclass to register what the
/// Obsidian client does with it, like its payload handlers, key bindings, block tints and entity renderers.
/// </summary>
/// <remarks>
/// Only the Obsidian client calls <see cref="ConfigureClient"/>, after the mod's
/// <see cref="Plugins.PluginBase.ConfigureRegistry"/>; servers never do, so a mod whose side is <c>both</c> runs on a
/// server without any client code. Keep client-only work in this method and what it registers.
/// </remarks>
public interface IClientPlugin
{
    /// <summary>
    /// Registers the mod's client side. Called when the client loads its mods: at startup, and again on a new
    /// instance of the plugin when the player reloads them. What it registers takes effect when it returns, after
    /// which <paramref name="registry"/> takes no more registrations, and is dropped when the mods reload.
    /// </summary>
    public void ConfigureClient(IClientRegistry registry);
}
