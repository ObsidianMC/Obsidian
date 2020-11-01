using Obsidian.API.Plugins;
using System;

namespace Obsidian.Plugins
{
    public class ScheduledDependencyInjection
    {
        public string Name { get; }
        public DependencyAttribute Info { get; }
        public Action<PluginBase> Inject { get; }

        public ScheduledDependencyInjection(string name, DependencyAttribute attribute, Action<PluginBase> injection)
        {
            Name = name;
            Info = attribute;
            Inject = injection;
        }
    }
}
