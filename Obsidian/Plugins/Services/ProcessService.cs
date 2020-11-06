using Obsidian.API.Plugins.Services.Diagnostics;
using Obsidian.Plugins.ServiceProviders;
using System;
using System.Diagnostics;
using System.Security;

namespace Obsidian.Plugins.Services
{
    public class ProcessService : IProcess
    {
        private readonly Process process;
        private readonly SecuredServiceBase serviceBase;

        public ProcessService(Process process, SecuredServiceBase serviceBase)
        {
            this.process = process;
            this.serviceBase = serviceBase;
        }

        public string Name => process.ProcessName;

        public int Id => process.Id;

        public DateTime StartTime => process.StartTime;

        public DateTime ExitTime => process.ExitTime;

        public int ThreadCount => process.Threads.Count;

        public long VirtualMemorySize => process.VirtualMemorySize64;

        public long PeakVirtualMemorySize => process.PeakVirtualMemorySize64;

        public long PagedMemorySize => process.PagedMemorySize64;

        public long NonpagedSystemMemorySize => process.NonpagedSystemMemorySize64;

        public long PagedSystemMemorySize => process.PagedSystemMemorySize64;

        public long PeakPagedMemorySize => process.PeakPagedMemorySize64;

        public long WorkingSet => process.WorkingSet64;

        public long PeakWorkingSet => process.PeakWorkingSet64;

        public long PrivateMemorySize => process.PrivateMemorySize64;

        public void Close()
        {
            if (!serviceBase.IsUsable)
                throw new SecurityException();

            process.Close();
        }

        public void Kill()
        {
            if (!serviceBase.IsUsable)
                throw new SecurityException();

            process.Kill();
        }

        public void Dispose()
        {
            process.Dispose();
        }
    }
}
