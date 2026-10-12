using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.Plugins;
using Obsidian.Registries;
using System.Threading;

namespace Obsidian.WorldData.Generators;

/// <summary>
/// The generator of a level that generates with a plugin's generator type (see <see cref="PluginGenerators"/>), or
/// with a generator nothing registered, which a plugin may register later. The level keeps this generator for its
/// life, and saves its identifier; the plugin's generator in it comes and goes with the plugin.
/// </summary>
/// <remarks>
/// <para>
/// Without the plugin's generator (not registered yet, its plugin unloaded, or its code failed), the level is stopped:
/// chunks it generated still load, and chunks it hasn't are left ungenerated until it starts again.
/// </para>
/// <para>
/// Each call into the plugin's generator holds a call into the plugin, a chunk at a time, so unloading the plugin
/// waits for chunks being generated. The plugin's generator is swapped between chunks: a chunk generating while it's
/// replaced finishes with the generator it started with.
/// </para>
/// </remarks>
/// <param name="identifier">The generator's identifier, which the level saves.</param>
internal sealed partial class PluginLevelGenerator(string identifier) : ILevelGenerator
{
    private Running? running;

    // The plugin whose generator failed, until it unloads: its generator stays stopped until then.
    private volatile PluginContainer? failedBy;

    private ILevel? level;

    public string Id { get; } = identifier;

    /// <summary>
    /// The plugin generating the level, or null when it's stopped.
    /// </summary>
    internal PluginContainer? Plugin => Volatile.Read(ref this.running)?.Plugin;

    /// <summary>
    /// The plugin's generator, or null when the level is stopped.
    /// </summary>
    internal ILevelGenerator? Generator => Volatile.Read(ref this.running)?.Generator;

    private ILogger Logger => (this.level as AbstractLevel)?.Logger ?? NullLogger.Instance;

    private string LevelName => this.level?.Name ?? this.Id;

    public void Init(ILevel level)
    {
        this.level = level;

        if (PluginGenerators.Find(this.Id) is PluginGenerator registered)
            this.Start(registered);
        else
            Log.Missing(this.Logger, this.LevelName, this.Id);
    }

    /// <summary>
    /// Starts generating with <paramref name="registered"/>, a new instance of the plugin's generator, unless it's
    /// generating already or the plugin's generator failed and the plugin hasn't reloaded since.
    /// </summary>
    /// <returns>Whether it generates with it.</returns>
    internal bool Start(PluginGenerator registered)
    {
        if (this.level is null || registered.Plugin == this.failedBy)
            return false;

        if (Volatile.Read(ref this.running) is Running current)
            return current.Plugin == registered.Plugin;

        using var call = registered.Plugin.TryEnter();
        if (call is null)
            return false;

        Running started;

        try
        {
            var generator = (ILevelGenerator)ActivatorUtilities.CreateInstance(
                registered.Plugin.ServiceProvider,
                registered.Type);

            generator.Init(this.level);
            started = new Running(generator, registered.Plugin);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            this.failedBy = registered.Plugin;
            Log.Failed(this.Logger, ex, this.LevelName, registered.Plugin.Info.Name);

            return false;
        }

        Volatile.Write(ref this.running, started);
        Log.Started(this.Logger, this.LevelName, this.Id, registered.Plugin.Info.Name);

        return true;
    }

    /// <summary>
    /// Stops generating with an unloading plugin's generator, and lets go of it so the plugin can unload. Chunks being
    /// generated finish first, as they hold calls into the plugin.
    /// </summary>
    /// <returns>Whether the level was generating with the plugin's generator.</returns>
    internal bool Stop(PluginContainer plugin)
    {
        if (this.failedBy == plugin)
            this.failedBy = null;

        var current = Volatile.Read(ref this.running);

        return current?.Plugin == plugin
            && Interlocked.CompareExchange(ref this.running, null, current) == current;
    }

    /// <remarks>
    /// Once stopped, a chunk is returned as it was given, ungenerated; one the plugin's generator fails on is returned
    /// empty and ungenerated instead, without what it wrote.
    /// </remarks>
    public async ValueTask<IChunk> GenerateChunkAsync(int x, int z, IChunk? chunk = null,
        ChunkGenStage stage = ChunkGenStage.full)
    {
        chunk ??= this.EmptyChunk(x, z);

        // Read once, so a swap applies from the next chunk.
        if (Volatile.Read(ref this.running) is not Running current)
            return chunk;

        using var call = current.Plugin.TryEnter();
        if (call is null)
            return chunk;

        try
        {
            return await current.Generator.GenerateChunkAsync(x, z, chunk, stage);
        }
        catch (Exception ex) when (this.Contain(current, ex))
        {
            return this.EmptyChunk(x, z);
        }
    }

    public async ValueTask<VectorD?> FindSpawnPointAsync()
    {
        if (Volatile.Read(ref this.running) is not Running current)
            return null;

        using var call = current.Plugin.TryEnter();
        if (call is null)
            return null;

        try
        {
            return await current.Generator.FindSpawnPointAsync();
        }
        catch (Exception ex) when (this.Contain(current, ex))
        {
            return null;
        }
    }

    public async ValueTask<IDisposable?> LockChunkAsync(int x, int z)
    {
        if (Volatile.Read(ref this.running) is not Running current)
            return null;

        using var call = current.Plugin.TryEnter();
        if (call is null)
            return null;

        try
        {
            return await current.Generator.LockChunkAsync(x, z);
        }
        catch (Exception ex) when (this.Contain(current, ex))
        {
            return null;
        }
    }

    // Contains a failure of the plugin's generator, which would otherwise end chunk management or the server: it's
    // logged, and the level stops until the plugin reloads. Only the first failure of a generator is logged.
    private bool Contain(Running failed, Exception exception)
    {
        if (exception is OutOfMemoryException)
            return false;

        if (Interlocked.CompareExchange(ref this.running, null, failed) == failed)
        {
            this.failedBy = failed.Plugin;
            Log.Failed(this.Logger, exception, this.LevelName, failed.Plugin.Info.Name);
        }

        return true;
    }

    private Chunk EmptyChunk(int x, int z) => this.level is AbstractLevel level
        ? new Chunk(x, z, level.MinY, level.Height, ChunkGenStage.structure_starts)
        : new Chunk(x, z, ChunkGenStage.structure_starts);

    private sealed record Running(ILevelGenerator Generator, PluginContainer Plugin);

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "{Level} won't generate new chunks until a plugin registers its generator, {Generator}")]
        public static partial void Missing(ILogger logger, string level, string generator);

        [LoggerMessage(Level = LogLevel.Information, Message = "{Level} generates with {Generator} from {Plugin}")]
        public static partial void Started(ILogger logger, string level, string generator, string plugin);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "{Level} stopped generating new chunks until {Plugin} reloads: its generator failed")]
        public static partial void Failed(ILogger logger, Exception exception, string level, string plugin);
    }
}
