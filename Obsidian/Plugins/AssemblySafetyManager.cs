using Obsidian.API.Plugins;
using System.Reflection;

namespace Obsidian.Plugins;

internal static class AssemblySafetyManager
{
    private static readonly byte[] systemKey; // shared between multiple assemblies
    private static readonly byte[] reflectionKey;
    private static readonly byte[] diagnosticsKey;
    private static readonly byte[] csharpKey;

    static AssemblySafetyManager()
    {
        systemKey = typeof(System.IO.DriveType).Assembly.GetName().GetPublicKeyToken();
        reflectionKey = typeof(BindingFlags).Assembly.GetName().GetPublicKeyToken();
        diagnosticsKey = typeof(System.Diagnostics.ActivityIdFormat).Assembly.GetName().GetPublicKeyToken();
        csharpKey = typeof(Microsoft.CodeAnalysis.DiagnosticSeverity).Assembly.GetName().GetPublicKeyToken();
    }

    public static PluginPermissions GetNeededPermissions(Assembly assembly)
    {
        var permissions = PluginPermissions.None;
        var references = assembly.GetReferencedAssemblies();
        foreach (var reference in references)
        {
            permissions |= GetNeededPermission(reference);
        }
        return permissions;
    }

    public static PluginPermissions GetNeededPermission(AssemblyName assemblyName)
    {
        if (MatchKey(assemblyName, systemKey))
        {
            if (assemblyName.Name.StartsWith("System.IO"))
                return PluginPermissions.FileAccess;
            if (assemblyName.Name.StartsWith("System.Net"))
                return PluginPermissions.NetworkAccess;
            if (assemblyName.Name.StartsWith("System.Runtime.Interop"))
                return PluginPermissions.Interop;
        }
        else if (MatchKey(assemblyName, reflectionKey))
        {
            if (assemblyName.Name.StartsWith("System.Reflection"))
                return PluginPermissions.Reflection;
        }
        else if (MatchKey(assemblyName, diagnosticsKey))
        {
            if (assemblyName.Name.StartsWith("System.Diagnostics") && assemblyName.Name != "System.Diagnostics.Debug")
                return PluginPermissions.RunningSubprocesses;
        }
        else if (MatchKey(assemblyName, csharpKey))
        {
            if (assemblyName.Name.StartsWith("Microsoft."))
                return PluginPermissions.Compilation;
        }
        else if (assemblyName.Name != "Obsidian.API")
        {
            return PluginPermissions.ThirdPartyLibraries;
        }

        return PluginPermissions.None;
    }

    private static bool MatchKey(AssemblyName assemblyName, byte[] key)
    {
        var assemblyKey = assemblyName.GetPublicKeyToken();

        if (assemblyKey.Length != key.Length) return false;
        for (int i = 0; i < key.Length; i++)
        {
            if (assemblyKey[i] != key[i])
                return false;
        }
        return true;
    }
}
