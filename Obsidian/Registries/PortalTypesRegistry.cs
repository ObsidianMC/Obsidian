using Obsidian.API.World;
using Obsidian.Plugins;
using Obsidian.WorldData;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>Process-lifetime identities with bindings and collectible callbacks released on unload.</summary>
internal static class PortalTypesRegistry
{
    private sealed record Binding(PortalType Type, PortalProperties Properties, PluginContainer? Owner);
    private static readonly Lock gate = new();
    private static readonly Dictionary<string, PortalType> identities = [];
    private static readonly Dictionary<string, Binding> bindings = [];

    internal static PortalType Nether { get; } = Register(null, "minecraft:nether", new PortalProperties
    {
        IsFrame = block => block.RegistryId == BlockIds.Obsidian,
        FrameBlock = BlocksRegistry.ObsidianBlock,
        PortalBlock = BlocksRegistry.NetherPortal,
        IgnitesFromFire = true,
        ResolveTarget = source =>
        {
            var world = source is IDimension child ? child.ParentWorld as World : source as World;
            if (world is null || source.DimensionName is not ("minecraft:overworld" or "minecraft:the_nether"))
                return null;

            return source.DimensionName == "minecraft:the_nether"
                ? world
                : world.GetLevel("minecraft:the_nether");
        },
        SearchRadius = target => target.DimensionName == "minecraft:the_nether" ? 16 : 128
    });

    internal static PortalType Register(PluginContainer? owner, string identifier, PortalProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (properties.MinWidth < 2 || properties.MinHeight < 3
            || properties.MaxWidth < properties.MinWidth || properties.MaxWidth > 21
            || properties.MaxHeight < properties.MinHeight || properties.MaxHeight > 21
            || properties.WaitTicks < 0 || properties.CreativeWaitTicks < 0 || properties.EntityWaitTicks < 0)
            throw new ArgumentException("Portal limits must fit a 2–21 by 3–21 interior and nonnegative waits.");

        ArgumentNullException.ThrowIfNull(properties.IsFrame);
        ArgumentNullException.ThrowIfNull(properties.ResolveTarget);
        ArgumentNullException.ThrowIfNull(properties.FrameBlock);
        ArgumentNullException.ThrowIfNull(properties.PortalBlock);
        if (properties.PortalBlock.WithProperty("axis", "x").GetProperty("axis") != "x"
            || properties.PortalBlock.WithProperty("axis", "z").GetProperty("axis") != "z")
            throw new ArgumentException("PortalBlock needs an axis property with x and z values.");

        using var call = owner?.TryEnter();
        if (owner is not null && call is null)
            throw new InvalidOperationException("The portal's owner is unloading.");

        if (!properties.IsFrame(properties.FrameBlock))
            throw new ArgumentException("FrameBlock must satisfy IsFrame.");

        lock (gate)
        {
            owner?.ThrowIfClosing();
            if (bindings.Values.Any(binding => binding.Type.Identifier != identifier
                && binding.Properties.PortalBlock.RegistryId == properties.PortalBlock.RegistryId))
                throw new ArgumentException("An active portal already uses this portal block.");

            if (bindings.TryGetValue(identifier, out var previous)
                && previous.Owner?.Info.Id != owner?.Info.Id)
                throw new ArgumentException("Another plugin owns this portal.");

            if (!identities.TryGetValue(identifier, out var type))
                identities[identifier] = type = new PortalType(identifier);

            type.Generation++;
            bindings[identifier] = new Binding(type, properties, owner);
            return type;
        }
    }

    internal static PortalType[] ActiveTypes()
    {
        lock (gate)
            return bindings.Values.Where(binding => binding.Owner is not { IsClosing: true })
                .Select(binding => binding.Type).ToArray();
    }

    internal static PortalType? ForBlock(int id)
    {
        lock (gate)
            return bindings.Values.FirstOrDefault(binding => binding.Properties.PortalBlock.RegistryId == id
                && binding.Owner is not { IsClosing: true })?.Type;
    }

    internal static bool TryEnter(PortalType type, out PortalProperties properties, out IDisposable? call)
    {
        lock (gate)
        {
            call = null;
            properties = null!;
            if (!bindings.TryGetValue(type.Identifier, out var binding))
                return false;

            if (binding.Owner is PluginContainer owner)
            {
                call = owner.TryEnter();
                if (call is null)
                    return false;
            }

            properties = binding.Properties;
            return true;
        }
    }

    internal static bool IsActive(PortalType type, PortalProperties properties)
    {
        lock (gate)
            return bindings.TryGetValue(type.Identifier, out var binding)
                && ReferenceEquals(binding.Properties, properties) && binding.Owner is not { IsClosing: true };
    }

    internal static void ReleasePlugin(PluginContainer owner)
    {
        lock (gate)
        {
            foreach (var key in bindings.Where(pair => pair.Value.Owner == owner).Select(pair => pair.Key).ToArray())
            {
                bindings[key].Type.Generation++;
                bindings.Remove(key);
            }
        }
    }
}
