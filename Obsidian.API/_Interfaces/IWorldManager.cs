using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API;
/// <summary>
/// Loads, ticks and saves the server's worlds.
/// </summary>
public interface IWorldManager : IAsyncDisposable
{
    /// <summary>
    /// Gets a value indicating whether the worlds have loaded and players can join.
    /// </summary>
    public bool ReadyToJoin { get; }

    /// <summary>
    /// Gets the number of chunks waiting to be generated, across every world.
    /// </summary>
    public int GeneratingChunkCount { get; }
    /// <summary>
    /// Gets the number of generated chunks loaded, across every world.
    /// </summary>
    public int LoadedChunkCount { get; }

    /// <summary>
    /// Gets the number of regions loaded, across every world.
    /// </summary>
    public int RegionCount { get; }

    /// <summary>
    /// Gets the world players join into.
    /// </summary>
    public IWorld DefaultWorld { get; }

    /// <summary>
    /// Gets a snapshot of the loaded worlds.
    /// </summary>
    public IReadOnlyCollection<IWorld> GetAvailableWorlds();

    /// <summary>
    /// Saves every loaded world: its and its dimensions' regions, then its level data and maps.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Called from inside a level's tick, or a player's tick, grant or save (a plugin's event handler run by one, say):
    /// it would wait for a tick that may be waiting for it. Plugins should save outside ticks: from a scheduled task or
    /// a command, say.
    /// </exception>
    public Task FlushLoadedWorldsAsync();

    /// <summary>
    /// Ticks every loaded world once. The server does, every tick.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Called from inside a level's tick, or a player's tick, grant or save: it would wait for a tick that may be
    /// waiting for it.
    /// </exception>
    public Task TickWorldsAsync();

    /// <summary>
    /// Gets the loaded world named <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The world's name.</param>
    /// <param name="world">The world, or <see langword="null"/> if none is loaded with that name.</param>
    /// <returns><see langword="true"/> if the world was found; otherwise, <see langword="false"/>.</returns>
    public bool TryGetWorld(string name, [NotNullWhen(true)] out IWorld? world);
    /// <summary>
    /// Gets the loaded world named <paramref name="name"/> as <typeparamref name="TWorld"/>.
    /// </summary>
    /// <typeparam name="TWorld">The world's type.</typeparam>
    /// <param name="name">The world's name.</param>
    /// <param name="world">The world, or the default value if none is loaded with that name.</param>
    /// <returns><see langword="true"/> if the world was found; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="InvalidCastException">The world isn't a <typeparamref name="TWorld"/>.</exception>
    public bool TryGetWorld<TWorld>(string name, [NotNullWhen(true)] out TWorld? world) where TWorld : IWorld;
}
