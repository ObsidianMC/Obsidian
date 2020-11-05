using Obsidian.API.Plugins;
using Obsidian.API.Plugins.Services;
using Obsidian.API.Plugins.Services.Diagnostics;
using Obsidian.Plugins.ServiceProviders;
using System.Diagnostics;
using System.Linq;
using System.Security;

namespace Obsidian.Plugins.Services
{
    public class DiagnoserService : SecuredServiceBase, IDiagnoser
    {
        internal override PluginPermissions NeededPermission => PluginPermissions.RunningSubprocesses;

        public DiagnoserService(PluginContainer plugin) : base(plugin)
        {
        }

        public IProcess GetProcess()
        {
            if (!IsUsable)
                throw new SecurityException(IDiagnoser.securityExceptionMessage);

            return new ProcessService(Process.GetCurrentProcess(), this);
        }

        public IProcess[] GetProcesses()
        {
            if (!IsUsable)
                throw new SecurityException(IDiagnoser.securityExceptionMessage);

            return Process.GetProcesses().Select(process => new ProcessService(process, this)).ToArray();
        }
    }
}
