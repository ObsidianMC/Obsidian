using Obsidian.API.World;
using System.Reflection;

namespace Obsidian.Plugins;

public sealed partial class PluginRegistry
{
    public PortalType RegisterPortal(string identifier, PortalProperties properties)
    {
        var owner = this.container;
        return RegisterPortal(owner, identifier, properties);
    }

    internal static PortalType RegisterPortal(PluginContainer owner, string identifier, PortalProperties properties)
    {
        CheckIdentifier(owner, identifier);
        lock (owner.WorldgenTypes)
        {
            if (!owner.RegisteringWorldgenTypes || owner.IsClosing)
                throw new InvalidOperationException("Portals must be registered during ConfigureRegistry.");

            return PortalTypesRegistry.Register(owner, identifier, properties);
        }
    }
}
