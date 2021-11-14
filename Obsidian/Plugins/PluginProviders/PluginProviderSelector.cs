using System;
using System.IO;

namespace Obsidian.Plugins.PluginProviders;

public static class PluginProviderSelector
{
    public static RemotePluginProvider RemotePluginProvider => remotePluginProvider ??= new RemotePluginProvider();
    public static UncompiledPluginProvider UncompiledPluginProvider => uncompiledPluginProvider ??= new UncompiledPluginProvider();
    public static CompiledPluginProvider CompiledPluginProvider => compiledPluginProvider ??= new CompiledPluginProvider();

    private static RemotePluginProvider remotePluginProvider;
    private static UncompiledPluginProvider uncompiledPluginProvider;
    private static CompiledPluginProvider compiledPluginProvider;

    public static IPluginProvider GetPluginProvider(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException();

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

    private static bool IsUrl(string path)
    {
        return Uri.TryCreate(path, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);
    }
}
