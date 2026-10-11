namespace Obsidian;

/// <summary>
/// The content a client configures with that plugins change (see <see cref="Server.GetContentVersion"/>).
/// </summary>
/// <param name="Registries">The sizes of the registries plugins added to.</param>
/// <param name="Channels">The plugins' payload channels.</param>
/// <param name="Tags">
/// How many times the tags were built (see <see cref="Obsidian.Registries.PluginTags.Revision"/>).
/// </param>
internal readonly record struct ContentVersion(string Registries, string Channels, int Tags);
