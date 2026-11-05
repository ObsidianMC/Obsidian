using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API;
public interface IWorldManager : IAsyncDisposable
{
    public bool ReadyToJoin { get; }

    public int GeneratingChunkCount { get; }
    public int LoadedChunkCount { get; }

    public int RegionCount { get; }

    public IWorld DefaultWorld { get; }

    public IReadOnlyCollection<IWorld> GetAvailableWorlds();

    /// <summary>
    /// Saves every loaded world: its and its dimensions' regions, then its level data and maps.
    /// </summary>
    /// <remarks>
    /// Like <see cref="ILevel.SaveAsync"/>, called from inside a level's tick, or a player's tick, grant or save, it
    /// saves in the background once the ticks are over and returns at once; a failure is then logged.
    /// </remarks>
    public Task FlushLoadedWorldsAsync();

    /// <summary>
    /// Ticks every loaded world once. The server does, every tick.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Called from inside a level's tick, or a player's tick, grant or save: it would wait for a tick that may be
    /// waiting for it.
    /// </exception>
    public Task TickWorldsAsync();

    public bool TryGetWorld(string name, [NotNullWhen(true)] out IWorld? world);
    public bool TryGetWorld<TWorld>(string name, [NotNullWhen(true)] out TWorld? world) where TWorld : IWorld;
}
