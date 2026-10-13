using Obsidian.API.Entities;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins.PluginProviders;
using Obsidian.WorldData;
using System.Diagnostics;

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

                if (assembly is { IsCollectible: true } && PluginLoadContext.OwnerOf(assembly) is PluginContainer owner)
                    return owner;
            }
        }

        return PluginCall.CurrentPlugin;
    }

    /// <summary>
    /// Credits an entity about to be added to a level to the plugin spawning it, unless it's a player or already the
    /// plugin's.
    /// </summary>
    /// <returns>The plugin the entity is credited to, or null.</returns>
    /// <exception cref="InvalidOperationException">The plugin is unloading.</exception>
    public static PluginContainer? Credit(IEntity entity)
    {
        if (entity is not Entity concrete || entity is IPlayer)
            return null;

        var spawner = concrete.SpawnedBy ?? FindSpawner();
        if (spawner is null)
            return null;

        spawner.ThrowIfClosing();
        concrete.SpawnedBy = spawner;

        return spawner;
    }

    /// <summary>
    /// Whether an entity's type is loaded: a vanilla type, or a plugin's type whose plugin is loaded and not unloading.
    /// </summary>
    public static bool HasLoadedType(IEntity entity) =>
        entity.Definition is not EntityDefinition definition
        || definition.Binding is { Owner: var owner } && owner is not PluginContainer { IsClosing: true };

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
    /// <param name="types">The plugin's entity types, released as it unloaded.</param>
    public static void RemoveFrom(AbstractLevel level, PluginContainer plugin, ImmutableArray<EntityDefinition> types)
    {
        var removed = new List<int>();

        foreach (var region in level.Regions.Values.OfType<Region>())
        {
            foreach (var entity in region.Entities.Values)
            {
                // Server players leave like other players, rather than being taken out of the level.
                if (entity is IPlayer)
                    continue;

                var ownType = entity.Definition is EntityDefinition definition
                    && types.Contains(definition)
                    && !HasLoadedType(entity);

                if (!ownType && !IsSpawnedBy(entity, plugin))
                    continue;

                if (region.Evict(entity, keepSaved: ownType))
                    removed.Add(entity.EntityId);
            }
        }

        if (removed.Count == 0)
            return;

        // Like vanilla's removal, to the level's players; they stop tracking the entities, so that tracking doesn't
        // remove them again.
        foreach (var player in level.Players.Values.OfType<Player>())
        {
            foreach (var id in removed)
                player.TrackedEntities.TryRemove(id, out _);
        }

        level.PacketBroadcaster.QueuePacketToLevel(level, new RemoveEntitiesPacket([.. removed]));
    }

    // Whether the plugin spawned the entity, or the entity is of a class of the plugin's.
    private static bool IsSpawnedBy(IEntity entity, PluginContainer plugin) =>
        entity is Entity { SpawnedBy: var spawner } && spawner == plugin
        || PluginLoadContext.OwnerOf(entity.GetType().Assembly) == plugin;
}
