using Obsidian.API.Plugins;
using Obsidian.API.Plugins.Services.Common;

namespace Obsidian.Plugins.ServiceProviders
{
    public abstract class SecuredServiceBase : ISecuredService
    {
        internal abstract PluginPermissions NeededPermission { get; }
        
        internal bool HasPermission { get; set; }
        public bool IsUsable => HasPermission;

        protected PluginContainer plugin;

        public SecuredServiceBase(PluginContainer plugin)
        {
            this.plugin = plugin;
        }
    }
}
