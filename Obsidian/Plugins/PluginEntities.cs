using Obsidian.API.Entities;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins.PluginProviders;
using Obsidian.WorldData;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Obsidian.Plugins;

/// <summary>
/// The entities plugins own: those a plugin's code spawned, and those of its entity types, whoever spawned them. Their
/// types or code may come from the plugin's load context, so unloading the plugin removes them from the levels.
/// </summary>
/// <remarks>
/// <para>
/// An entity is the plugin's whose code adds it to a level (<see cref="FindSpawner"/>), like dialogs are: entities
/// are spawned through many shared objects (levels reached from players, entities and event arguments, entities
/// spawning others), which the plugin's own <see cref="IServer"/> can't stand in for. Server code a plugin's code
/// calls synchronously, like a block break dropping items, spawns for the plugin too.
/// </para>
/// <para>
/// Server players a plugin adds are its own the same way, or through its own <see cref="IServer"/>, and leave as other
/// players do when it unloads.
/// </para>
/// </remarks>
internal static class PluginEntities
{
    private sealed record Registration(PluginOwnership? Spawner, PluginOwnership? TypeOwner);

    // Works for custom IEntity implementations too, without keeping entities or plugin generations alive.
    private static readonly ConditionalWeakTable<IEntity, Registration> registrations = new();

    /// <summary>
    /// The plugin whose code is running now: the innermost of the plugins' code on the call stack, such as a plugin
    /// spawning from its ready callback or a task of its own, or else the plugin whose call the async flow is in (see
    /// <see cref="PluginCall.CurrentPlugin"/>), as when server code awaited by a plugin's handler continues.
    /// </summary>
    /// <returns>The plugin, or null for the server's code.</returns>
    public static PluginContainer? FindSpawner()
    {
        // Walking the stack is the costly part, so it's skipped while no plugin's code is loaded.
        if (PluginLoadContext.AnyLoaded)
        {
            foreach (var frame in new StackTrace(1, fNeedFileInfo: false).GetFrames())
            {
                var assembly = frame.GetMethod()?.Module.Assembly;

                if (assembly is { IsCollectible: true }
                    && PluginLoadContext.OwnerOf(assembly) is PluginContainer owner)
                {
                    owner.ThrowIfClosing();
                    return owner;
                }
            }
        }

        return PluginCall.CurrentOwnership?.RequireActive();
    }

    /// <summary>
    /// Credits an entity about to be added to a level to the plugin spawning it, unless it's a player or already the
    /// plugin's.
    /// </summary>
    /// <returns>The plugin the entity is credited to, or null.</returns>
    /// <exception cref="InvalidOperationException">The plugin is unloading.</exception>
    public static PluginContainer? Credit(IEntity entity, bool hostOwned = false)
    {
        if (entity is IPlayer)
            return null;

        if (registrations.TryGetValue(entity, out var registered))
            return registered.Spawner?.RequireActive();

        var spawner = (entity as Entity)?.SpawnedBy ?? (hostOwned ? null : FindSpawner());
        spawner?.ThrowIfClosing();

        var typeOwner = (entity.Definition?.Owner as PluginContainer)?.Ownership;
        if (spawner is not null || typeOwner is not null)
        {
            var registration = registrations.GetValue(entity, _ => new Registration(spawner?.Ownership, typeOwner));
            spawner = registration.Spawner?.RequireActive();
        }

        if (entity is Entity concrete)
            concrete.SpawnedBy = spawner;

        return spawner;
    }

    /// <summary>
    /// Whether the implementation and registered type still belong to a loaded plugin generation.
    /// </summary>
    public static bool HasLoadedType(IEntity entity)
    {
        if (PluginLoadContext.OwnerOf(entity.GetType().Assembly) is { IsClosing: true })
            return false;

        if (entity.Definition is not EntityDefinition definition)
            return true;

        if (definition.Binding is not { Owner: var owner } || owner is PluginContainer { IsClosing: true })
            return false;

        return !registrations.TryGetValue(entity, out var registration)
            || registration.TypeOwner == (owner as PluginContainer)?.Ownership;
    }

    /// <summary>
    /// Removes from a level the entities <paramref name="plugin"/> owns, as it unloads, and tells the players there
    /// they're gone. Entities of its types are kept in their chunks as they were saved, like saved entities of types
    /// the server doesn't know, and spawn again once the plugin is loaded again (see
    /// <see cref="Region.RespawnKeptEntities"/>); the others are gone.
    /// </summary>
    /// <remarks>
    /// Run between the level's ticks, so that no entity is mid-tick, moving to another region. Removing calls nothing
    /// of the plugin's: its behaviours were released, and no event is raised.
    /// </remarks>
    /// <param name="level">The level.</param>
    /// <param name="plugin">The unloading plugin.</param>
    public static void RemoveFrom(AbstractLevel level, PluginContainer plugin)
    {
        var removed = new HashSet<IEntity>(ReferenceEqualityComparer.Instance);

        foreach (var region in level.Regions.Values.OfType<Region>())
        {
            foreach (var entity in region.Entities.Values)
            {
                // Server players leave like other players, rather than being taken out of the level.
                if (entity is IPlayer)
                    continue;

                var ownType = registrations.TryGetValue(entity, out var registration)
                    && registration.TypeOwner == plugin.Ownership;

                if (!ownType && !IsSpawnedBy(entity, plugin))
                    continue;

                if (region.Evict(entity, keepSaved: ownType))
                    removed.Add(entity);
            }

            // Reload may have rebound the definition before this deferred removal ran.
            region.RespawnKeptEntities(level);
        }

        if (removed.Count == 0)
            return;

        foreach (var entity in level.Regions.Values.SelectMany(region => region.Entities.Values).OfType<Entity>())
        {
            if (entity is Mob mob)
                mob.ForgetEvictedEntities(removed);

            if (entity.Navigator is Entities.AI.Navigator navigator && navigator.Target is IEntity target
                && removed.Contains(target))
            {
                navigator.ClearForUnload();
            }
        }

        var ids = removed.Select(entity => entity.EntityId).ToArray();

        // Like vanilla's removal, to the level's players; they stop tracking the entities, so that tracking doesn't
        // remove them again.
        foreach (var player in level.Players.Values)
        {
            foreach (var id in ids)
                player.UntrackEntity(id);
        }

        level.PacketBroadcaster.QueuePacketToLevel(level, new RemoveEntitiesPacket(ids));
    }

    // Whether the plugin spawned the entity, or the entity is of a class of the plugin's.
    private static bool IsSpawnedBy(IEntity entity, PluginContainer plugin) =>
        entity is Entity { SpawnedBy: var spawner } && spawner == plugin
        || registrations.TryGetValue(entity, out var registration) && registration.Spawner == plugin.Ownership
        || PluginLoadContext.OwnerOf(entity.GetType().Assembly) == plugin;
}
