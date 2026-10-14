namespace Obsidian.Plugins;

public sealed partial class PluginManager
{
    private void ApplyRecipeFiles()
    {
        if (this.server is Server server)
        {
            server.LoadRecipeFiles(this.plugins.Select(plugin =>
                (plugin, (IEnumerable<Registries.RecipeFile>)plugin.RecipeFiles)));
        }
    }
}
