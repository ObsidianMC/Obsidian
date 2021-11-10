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
                throw new SecurityException(IDiagnoser.SecurityExceptionMessage);

            return new ProcessService(Process.GetCurrentProcess(), this);
        }

        public IProcess[] GetProcesses()
        {
            if (!IsUsable)
                throw new SecurityException(IDiagnoser.SecurityExceptionMessage);

            return Process.GetProcesses().Select(process => new ProcessService(process, this)).ToArray();
        }

        public IProcess StartProcess(string fileName, string? arguments = null, bool createWindow = true, bool useShell = false)
        {
            if (!IsUsable)
                throw new SecurityException(IDiagnoser.SecurityExceptionMessage);

            var processInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                CreateNoWindow = !createWindow,
                UseShellExecute = useShell
            };
            var process = Process.Start(processInfo);
            return process is null ? null : new ProcessService(process, this);
        }

        public IStopwatch GetStopwatch()
        {
            return new StopwatchService(start: false);
        }

        public IStopwatch StartStopwatch()
        {
            return new StopwatchService(start: true);
        }
    }
}
