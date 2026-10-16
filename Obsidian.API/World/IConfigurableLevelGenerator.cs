using System.Text.Json;

namespace Obsidian.API.World;

/// <summary>
/// A level generator that takes settings from the level stem naming it as its <c>generator</c>, like
/// <c>{"type": "ruby:sky_islands", "island_size": 12}</c> in <c>data/ruby/dimension/islands.json</c>.
/// </summary>
/// <remarks>
/// Optional: a generator that doesn't implement it ignores the stem's settings. Levels that don't come from a level
/// stem, like a world's in <c>worlds.json</c>, never call it.
/// </remarks>
public interface IConfigurableLevelGenerator : ILevelGenerator
{
    /// <summary>
    /// Gives the generator its level stem's settings. It's called once on each new instance, before
    /// <see cref="ILevelGenerator.Init"/>, so again when the level resumes with a new instance after its plugin
    /// reloaded.
    /// </summary>
    /// <param name="settings">The stem's <c>generator</c> object, its <c>type</c> included. It stays valid after the
    /// call.</param>
    /// <exception cref="Exception">Settings the generator can't use: the level stops generating, logged, like when
    /// <see cref="ILevelGenerator.Init"/> throws.</exception>
    public void Configure(JsonElement settings);
}
