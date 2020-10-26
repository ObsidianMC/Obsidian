using Obsidian.API.Plugins;
using System;

namespace Obsidian.Plugins
{
    public class ScheduledDependencyInjection
    {
        public string Name { get; }
        public DependencyAttribute Info { get; }
        private Action<PluginBase> Injection { get; }

        public ScheduledDependencyInjection(string name, DependencyAttribute attribute, Action<PluginBase> injection)
        {
            Name = name;
            Info = attribute;
            Injection = injection;
        }

        public void Inject(PluginBase dependency)
        {
            Injection.Invoke(dependency);
        }
    }
}
