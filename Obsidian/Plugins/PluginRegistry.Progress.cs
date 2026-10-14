using Obsidian.API.Advancements;
using System.Reflection;

namespace Obsidian.Plugins;

public sealed partial class PluginRegistry
{
    public int RegisterCustomStatistic(string identifier)
    {
        var owner = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());
        lock (owner.WorldgenTypes)
        {
            if (!owner.RegisteringWorldgenTypes || owner.IsClosing)
                throw new InvalidOperationException("Statistics must be registered during ConfigureRegistry.");

            return CustomStatisticsRegistry.Register(owner, identifier);
        }
    }

    public AdvancementTrigger<TInstance> RegisterAdvancementTrigger<TInstance>(
        string identifier, AdvancementConditionsCodec<TInstance> conditions)
    {
        var owner = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());
        lock (owner.WorldgenTypes)
        {
            if (!owner.RegisteringWorldgenTypes || owner.IsClosing)
                throw new InvalidOperationException("Triggers must be registered during ConfigureRegistry.");

            return AdvancementTriggersRegistry.Register(owner, identifier, conditions);
        }
    }
}
