using Obsidian.API.Plugins;
using Obsidian.Plugins.ServiceProviders;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;

namespace Obsidian.Plugins
{
    public class PluginContainer
    {
        public PluginBase Plugin { get; }
        public PluginInfo Info { get; }
        public string Source { get; internal set; }
        public Assembly Assembly { get; }
        public AssemblyLoadContext LoadContext { get; }

        private PluginPermissions _permissions;
        public PluginPermissions Permissions { get => _permissions; set => UpdatePermissions(value); }
        public PluginPermissions NeedsPermissions { get; }

        public event Action<PluginContainer> PermissionsChanged;

        public bool HasPermissions => (Permissions & NeedsPermissions) == NeedsPermissions;
        public bool HasDependencies => true;
        public bool IsReady => HasPermissions && HasDependencies;
        public bool Loaded { get; internal set; }

        internal Dictionary<PluginPermissions, WeakReference<SecuredServiceBase>> SecuredServices { get; } = new Dictionary<PluginPermissions, WeakReference<SecuredServiceBase>>();
        internal List<IDisposable> DisposableServices { get; } = new List<IDisposable>();
        internal Dictionary<EventContainer, Delegate> EventHandlers { get; } = new Dictionary<EventContainer, Delegate>();

        public PluginContainer(PluginBase plugin, PluginInfo info, Assembly assembly, AssemblyLoadContext loadContext, string source)
        {
            Plugin = plugin;
            Info = info;
            Assembly = assembly;
            LoadContext = loadContext;
            Source = source;

            NeedsPermissions = PluginPermissions.None;
            if (assembly != null)
            {
                AssemblyName[] referencedAssemblies = Assembly.GetReferencedAssemblies();
                for (int i = 0; i < referencedAssemblies.Length; i++)
                {
                    NeedsPermissions |= GetNeededAssemblyPermission(referencedAssemblies[i]);
                }
            }
        }

        public bool HasPermission(PluginPermissions permission)
        {
            if (permission == PluginPermissions.None)
                return true;

            return (permission & _permissions) != 0;
        }

        internal void RegisterSecuredService(SecuredServiceBase securedService)
        {
            if (!SecuredServices.ContainsKey(securedService.NeededPermission))
                SecuredServices.Add(securedService.NeededPermission, new WeakReference<SecuredServiceBase>(securedService));
        }

        internal void RegisterDisposableService(IDisposable disposableService)
        {
            DisposableServices.Add(disposableService);
        }

        private void UpdatePermissions(PluginPermissions permissions)
        {
            _permissions = permissions;
            foreach (var (key, value) in SecuredServices)
            {
                if (value.TryGetTarget(out var service))
                {
                    service.HasPermission = HasPermission(key);
                }
                else
                {
                    SecuredServices.Remove(key);
                }
            }
            PermissionsChanged?.Invoke(this);
        }

        private PluginPermissions GetNeededAssemblyPermission(AssemblyName assembly)
        {
            if (assembly.Name.StartsWith("System.IO"))
                return PluginPermissions.FileAccess;

            if (assembly.Name.StartsWith("System.Net"))
                return PluginPermissions.InternetAccess;

            if (assembly.Name.StartsWith("System.Reflection"))
                return PluginPermissions.Reflection;

            if (assembly.Name.StartsWith("System.Runtime.Interop"))
                return PluginPermissions.Interop;

            if (assembly.Name.StartsWith("System.Diagnostics") && assembly.Name != "System.Diagnostics.Debug")
                return PluginPermissions.RunningSubprocesses;

            if (assembly.Name.StartsWith("Microsoft.CSharp"))
                return PluginPermissions.Compilation;

            if (!assembly.Name.StartsWith("System") && !assembly.Name.StartsWith("Microsoft") && !assembly.Name.StartsWith("Obsidian.API"))
                return PluginPermissions.ThirdPartyLibraries;

            return PluginPermissions.None;
        }
    }
}
