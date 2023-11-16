using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API;
public interface IWorldManager : IAsyncDisposable
{
    public Dictionary<string, Type> WorldGenerators { get; }

    public int GeneratingChunkCount { get; }
    public int LoadedChunkCount { get; }

    public int RegionCount { get; }

    public IWorld DefaultWorld { get; }

    public IReadOnlyCollection<IWorld> GetAvailableWorlds();

    public Task FlushLoadedWorldsAsync();

    public Task TickWorldsAsync();

    public bool TryGetWorld(string name, [NotNullWhen(true)] out IWorld? world);
    public bool TryGetWorld<TWorld>(string name, [NotNullWhen(true)] out TWorld? world) where TWorld : IWorld;
}
