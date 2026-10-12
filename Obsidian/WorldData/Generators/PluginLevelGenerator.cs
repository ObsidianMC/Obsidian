using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.Plugins;
using Obsidian.Plugins.ServiceProviders;
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
/// <para>
/// Generating a chunk and saving it hold the chunk's lock, which this generator owns, so a save never captures a
/// chunk mid-write, whether a plugin's generator is running, being stopped or replaced, or stopped. While one runs,
/// a save also takes its own <see cref="ILevelGenerator.LockChunkAsync"/> lock, which a plugin's generator writing
/// into other chunks than the one it generates locks them with.
/// </para>
/// <para>
/// Each instance of a plugin's generator is created in a service scope of its own, and once it's stopped, replaced
/// or failed, it's disposed with its scope when the calls using it end (if it's <see cref="IDisposable"/> or
/// <see cref="IAsyncDisposable"/>). Until then its plugin can't be released.
/// </para>
/// </remarks>
/// <param name="identifier">The generator's identifier, which the level saves.</param>
internal sealed partial class PluginLevelGenerator(string identifier) : ILevelGenerator
{
    // Chunk locks are striped so their number stays bounded.
    private const int LockStripeCount = 1024;

    private readonly SemaphoreSlim[] chunkLocks =
        [.. Enumerable.Range(0, LockStripeCount).Select(_ => new SemaphoreSlim(1, 1))];

    private Instance? running;

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

        if (Volatile.Read(ref this.running) is Instance current)
            return current.Plugin == registered.Plugin;

        using var call = registered.Plugin.TryEnter();
        if (call is null)
            return false;

        var hold = registered.Plugin.TryHold();
        if (hold is null)
            return false;

        var services = registered.Plugin.CreateServiceScope();
        ILevelGenerator? generator = null;

        try
        {
            generator = (ILevelGenerator)ActivatorUtilities.CreateInstance(services, registered.Type);
            generator.Init(this.level);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            this.failedBy = registered.Plugin;
            Log.Failed(this.Logger, ex, this.LevelName, registered.Plugin.Info.Name);
            _ = DisposeAsync(generator, services, hold, this.Logger, registered.Plugin);

            return false;
        }

        var started = new Instance(generator, registered.Plugin, services, hold, this.Logger);
        if (Interlocked.CompareExchange(ref this.running, started, null) is not null)
        {
            started.Retire();
            return false;
        }

        Log.Started(this.Logger, this.LevelName, this.Id, registered.Plugin.Info.Name);

        return true;
    }

    /// <summary>
    /// Stops generating with an unloading plugin's generator, and lets go of it so the plugin can unload. Chunks being
    /// generated finish first, as they hold calls into the plugin; the generator is disposed after them.
    /// </summary>
    /// <returns>Whether the level was generating with the plugin's generator.</returns>
    internal bool Stop(PluginContainer plugin)
    {
        if (this.failedBy == plugin)
            this.failedBy = null;

        var current = Volatile.Read(ref this.running);
        if (current?.Plugin != plugin || Interlocked.CompareExchange(ref this.running, null, current) != current)
            return false;

        current.Retire();

        return true;
    }

    /// <remarks>
    /// Once stopped, a chunk is returned as it was given, ungenerated; one the plugin's generator fails on is returned
    /// empty and ungenerated instead, without what it wrote.
    /// </remarks>
    public async ValueTask<IChunk> GenerateChunkAsync(int x, int z, IChunk? chunk = null,
        ChunkGenStage stage = ChunkGenStage.full)
    {
        chunk ??= this.EmptyChunk(x, z);

        using var chunkLock = await this.LockAsync(x, z);

        // Read once, so a swap applies from the next chunk.
        if (Volatile.Read(ref this.running) is not Instance current)
            return chunk;

        using var use = current.TryUse();
        using var call = use is null ? null : current.Plugin.TryEnter();
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
        if (Volatile.Read(ref this.running) is not Instance current)
            return null;

        using var use = current.TryUse();
        using var call = use is null ? null : current.Plugin.TryEnter();
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

    /// <remarks>
    /// The chunk's own lock is always taken; the running generator's, if any, too. The returned lock keeps that
    /// generator, and so its plugin, from being disposed until it's released.
    /// </remarks>
    public async ValueTask<IDisposable?> LockChunkAsync(int x, int z)
    {
        var chunkLock = await this.LockAsync(x, z);

        if (Volatile.Read(ref this.running) is not Instance current || current.TryUse() is not IDisposable use)
            return chunkLock;

        try
        {
            using var call = current.Plugin.TryEnter();
            var generatorLock = call is null ? null : await current.Generator.LockChunkAsync(x, z);

            return new Locks(generatorLock, use, chunkLock);
        }
        catch (Exception ex) when (this.Contain(current, ex))
        {
            use.Dispose();
            return chunkLock;
        }
        catch
        {
            use.Dispose();
            chunkLock.Dispose();
            throw;
        }
    }

    // Contains a failure of the plugin's generator, which would otherwise end chunk management or the server: it's
    // logged, and the level stops until the plugin reloads. Only the first failure of a generator is logged.
    private bool Contain(Instance failed, Exception exception)
    {
        if (exception is OutOfMemoryException)
            return false;

        if (Interlocked.CompareExchange(ref this.running, null, failed) == failed)
        {
            this.failedBy = failed.Plugin;
            Log.Failed(this.Logger, exception, this.LevelName, failed.Plugin.Info.Name);
            failed.Retire();
        }

        return true;
    }

    private async ValueTask<IDisposable> LockAsync(int x, int z)
    {
        var stripe = this.chunkLocks[(uint)HashCode.Combine(x, z) % LockStripeCount];
        await stripe.WaitAsync();

        return new StripeLock(stripe);
    }

    private Chunk EmptyChunk(int x, int z) => this.level is AbstractLevel level
        ? new Chunk(x, z, level.MinY, level.Height, ChunkGenStage.structure_starts)
        : new Chunk(x, z, ChunkGenStage.structure_starts);

    // Disposes an instance of a plugin's generator, if it's disposable, then its services, then lets go of its plugin.
    // Failures are logged: the plugin's release must still go on.
    private static async Task DisposeAsync(
        ILevelGenerator? generator,
        PluginServiceProvider services,
        IDisposable hold,
        ILogger logger,
        PluginContainer plugin)
    {
        try
        {
            if (generator is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else if (generator is IDisposable disposable)
                disposable.Dispose();
        }
        catch (Exception ex)
        {
            Log.DisposeFailed(logger, ex, plugin.Info.Name);
        }

        try
        {
            await services.DisposeAsync();
        }
        catch (Exception ex)
        {
            Log.DisposeFailed(logger, ex, plugin.Info.Name);
        }
        finally
        {
            hold.Dispose();
        }
    }

    /// <summary>
    /// An instance of a plugin's generator. It's disposed once it's retired (stopped, replaced or failed) and the
    /// uses of it ended, and holds its plugin until then.
    /// </summary>
    private sealed class Instance(
        ILevelGenerator generator,
        PluginContainer plugin,
        PluginServiceProvider services,
        IDisposable hold,
        ILogger logger)
    {
        // One while it isn't retired, plus one per use.
        private int users = 1;
        private int retired;

        public ILevelGenerator Generator { get; } = generator;

        public PluginContainer Plugin { get; } = plugin;

        /// <summary>
        /// Starts using the instance, which keeps it from being disposed until the use is.
        /// </summary>
        /// <returns>The use, or null once it's disposed.</returns>
        public IDisposable? TryUse()
        {
            var count = Volatile.Read(ref this.users);

            while (count > 0)
            {
                var seen = Interlocked.CompareExchange(ref this.users, count + 1, count);
                if (seen == count)
                    return new Use(this);

                count = seen;
            }

            return null;
        }

        /// <summary>
        /// Lets go of the instance: it's disposed once its uses end.
        /// </summary>
        public void Retire()
        {
            if (Interlocked.Exchange(ref this.retired, 1) == 0)
                this.Exit();
        }

        private void Exit()
        {
            if (Interlocked.Decrement(ref this.users) == 0)
                _ = DisposeAsync(this.Generator, services, hold, logger, this.Plugin);
        }

        private sealed class Use(Instance instance) : IDisposable
        {
            private Instance? instance = instance;

            public void Dispose() => Interlocked.Exchange(ref this.instance, null)?.Exit();
        }
    }

    private sealed class StripeLock(SemaphoreSlim stripe) : IDisposable
    {
        private SemaphoreSlim? stripe = stripe;

        public void Dispose() => Interlocked.Exchange(ref this.stripe, null)?.Release();
    }

    // A save's locks: the running generator's own, its use, then the chunk's, released in that order.
    private sealed class Locks(IDisposable? generatorLock, IDisposable use, IDisposable chunkLock) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                generatorLock?.Dispose();
            }
            finally
            {
                use.Dispose();
                chunkLock.Dispose();
            }
        }
    }

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

        [LoggerMessage(Level = LogLevel.Warning, Message = "Disposing a generator of {Plugin} failed")]
        public static partial void DisposeFailed(ILogger logger, Exception exception, string plugin);
    }
}
