using System.IO;

namespace Obsidian.Plugins.PluginProviders;

public static class PluginProviderSelector
{
    public static RemotePluginProvider RemotePluginProvider { get; internal set; } = default!;
    public static UncompiledPluginProvider UncompiledPluginProvider { get; internal set; } = default!;
    public static CompiledPluginProvider CompiledPluginProvider { get; internal set; } = default!;


    public static IPluginProvider? GetPluginProvider(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (IsUrl(path))
        {
            return RemotePluginProvider;
        }
        else
        {
            var fileExtension = Path.GetExtension(path);
            return fileExtension switch
            {
                ".cs" => UncompiledPluginProvider,
                ".dll" => CompiledPluginProvider,
                _ => null
            };
        }
    }

    private static bool IsUrl(string path) =>
        Uri.TryCreate(path, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);
}
