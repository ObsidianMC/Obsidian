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
/// chunk mid-write, whether a plugin's generator is running, being stopped or replaced, or stopped. A plugin's
/// generator writing into other chunks than the one it generates locks them with its own
/// <see cref="ILevelGenerator.LockChunkAsync"/>, which a save takes too while it runs. Once it's stopped, replaced or
/// failed, its plugin may refuse calls into that lock, so saves wait for its calls to end instead, which is rare and
/// short. A newly started generator first waits for the saves already locking chunks, as they may not have seen it,
/// and holds new ones back meanwhile (see <see cref="LockChunkAsync"/>).
/// </para>
/// <para>
/// Each instance of a plugin's generator is created in a service scope of its own, and once it's stopped, replaced
/// or failed, it's disposed with its scope when the calls using it end (if it's <see cref="IDisposable"/> or
/// <see cref="IAsyncDisposable"/>). Until then its plugin can't be released. Disposing the level disposes this
/// generator (see <see cref="Dispose"/>), which retires its instance for good.
/// </para>
/// </remarks>
/// <param name="identifier">The generator's identifier, which the level saves.</param>
internal sealed partial class PluginLevelGenerator(string identifier) : ILevelGenerator, IDisposable
{
    // Chunk locks are striped so their number stays bounded.
    private const int LockStripeCount = 1024;

    // Guards starting and stopping plugins' generators in every level, and the referencing set.
    private static readonly Lock transitions = new();

    // The generators whose level generates with a plugin's generator, or stopped because it failed, so unloading the
    // plugin finds them even before their level is registered (see StopAll).
    private static readonly HashSet<PluginLevelGenerator> referencing = [];

    private readonly SemaphoreSlim[] chunkLocks =
        [.. Enumerable.Range(0, LockStripeCount).Select(_ => new SemaphoreSlim(1, 1))];

    // Replaced as a whole under transitions, so a save reads the running and retired instances together.
    private volatile Instances instances = Instances.None;

    // The plugin whose generator failed, until it unloads: its generator stays stopped until then. Set under
    // transitions.
    private volatile PluginContainer? failedBy;

    // Guards the count of held save handles (see LockChunkAsync). Taken after transitions, never across an await.
    private readonly Lock saveGate = new();
    private int saves;

    // Set while a newly started instance waits for the save handles held when it started, which new ones wait for too,
    // so they can't starve it. Completed and cleared once those are all released. Set under saveGate.
    private TaskCompletionSource? earlierSaves;

    // Set under transitions once the level is disposed: no plugin's generator starts in it any more.
    private volatile bool disposed;

    private ILevel? level;

    public string Id { get; } = identifier;

    /// <summary>
    /// The plugin generating the level, or null when it's stopped.
    /// </summary>
    internal PluginContainer? Plugin => this.instances.Running?.Plugin;

    /// <summary>
    /// The plugin's generator, or null when the level is stopped.
    /// </summary>
    internal ILevelGenerator? Generator => this.instances.Running?.Generator;

    /// <summary>
    /// Whether unloading plugins tracks this generator: while a plugin's generator runs in it, or failed.
    /// </summary>
    internal bool Tracked
    {
        get
        {
            lock (transitions)
                return referencing.Contains(this);
        }
    }

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
    /// Stops generating with an unloading plugin's generator in every level, including levels not registered yet,
    /// whose generator may have started while the plugin was closing. Call it once the plugin is closed.
    /// </summary>
    /// <returns>The names of the levels that were generating with it.</returns>
    internal static List<string> StopAll(PluginContainer plugin)
    {
        PluginLevelGenerator[] generators;
        lock (transitions)
            generators = [.. referencing];

        return [.. generators.Where(generator => generator.Stop(plugin)).Select(generator => generator.LevelName)];
    }

    /// <summary>
    /// Starts generating with <paramref name="registered"/>, a new instance of the plugin's generator, unless it's
    /// generating already, the plugin is closing, or the plugin's generator failed and the plugin hasn't reloaded
    /// since.
    /// </summary>
    /// <returns>Whether it generates with it.</returns>
    internal bool Start(PluginGenerator registered)
    {
        if (this.level is null || this.disposed || registered.Plugin == this.failedBy)
            return false;

        if (this.instances.Running is Instance current)
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
            lock (transitions)
            {
                // A closing plugin may have looked for this level already, and wouldn't clear the failure; nor would
                // anything once the level is disposed.
                if (!registered.Plugin.IsClosing && !this.disposed)
                {
                    this.failedBy = registered.Plugin;
                    referencing.Add(this);
                }
            }

            Log.Failed(this.Logger, ex, this.LevelName, registered.Plugin.Info.Name);
            _ = DisposeAsync(generator, services, hold, this.Logger, registered.Plugin);

            return false;
        }

        var started = new Instance(generator, registered.Plugin, services, hold, this.Logger, this.Forget);
        var published = false;

        lock (transitions)
        {
            // Checked under the lock StopAll reads the referencing set under: a plugin that closed while its
            // generator initialized either is seen closing here, or finds this level once it's published.
            if (this.instances.Running is null && !this.disposed && !registered.Plugin.IsClosing)
            {
                // Under the gate's lock, so each save handle is either held now, and waited for, or registers after
                // this and reads this instance as running.
                lock (this.saveGate)
                {
                    started.Ready = this.CloseSaveGate();
                    this.instances = this.instances with { Running = started };
                }

                referencing.Add(this);
                published = true;
            }
        }

        if (!published)
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
        Instance? stopped = null;

        lock (transitions)
        {
            if (this.failedBy == plugin)
                this.failedBy = null;

            if (this.instances.Running is Instance current && current.Plugin == plugin)
            {
                this.Detach(current);
                stopped = current;
            }

            if (this.instances.Running is null && this.failedBy is null)
                referencing.Remove(this);
        }

        stopped?.Retire();

        return stopped is not null;
    }

    /// <summary>
    /// Stops generating for good, as the level is disposed: no plugin's generator starts in it any more, and unloading
    /// plugins no longer track it. Its running instance is retired like a stopped one, so saves still wait for its
    /// calls. Call it once nothing generates in the level any more.
    /// </summary>
    public void Dispose()
    {
        Instance? stopped = null;

        lock (transitions)
        {
            this.disposed = true;
            this.failedBy = null;

            if (this.instances.Running is Instance current)
            {
                this.Detach(current);
                stopped = current;
            }

            referencing.Remove(this);
        }

        stopped?.Retire();
    }

    /// <remarks>
    /// Once stopped, a chunk is returned as it was given, ungenerated; one the plugin's generator fails on is returned
    /// empty and ungenerated instead, without what it wrote, and replaces the given one where its level stores it.
    /// </remarks>
    public async ValueTask<IChunk> GenerateChunkAsync(int x, int z, IChunk? chunk = null,
        ChunkGenStage stage = ChunkGenStage.full)
    {
        chunk ??= this.EmptyChunk(x, z);

        var (chunkLock, running) = await this.LockToGenerateAsync(x, z);

        using (chunkLock)
        {
            if (running is not Instance current)
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
                // Stored while the chunk's lock is still held, so a save waiting for it serializes the empty chunk
                // rather than what the generator wrote into the given one.
                var empty = this.EmptyChunk(x, z);
                if (this.level is AbstractLevel level && level.GetRegionForChunk(x, z) is Region region)
                    region.ReplaceChunk(chunk, empty);

                return empty;
            }
        }
    }

    public async ValueTask<VectorD?> FindSpawnPointAsync()
    {
        if (this.instances.Running is not Instance current)
            return null;

        await current.Ready;

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
    /// <para>
    /// The save registers a handle with this generator's save gate, then takes the chunk's own lock. Then it waits
    /// for the calls of retired instances to end, and takes the running instance's own lock, or waits for its calls
    /// too if its plugin is closing. The returned lock keeps that instance, and so its plugin, from being disposed,
    /// and the handle registered, until it's released.
    /// </para>
    /// <para>
    /// An instance starting meanwhile could write into the chunk as a neighbour, unseen by the save, so it doesn't
    /// generate until the handles held when it started are released, and new handles wait for that too. Neither
    /// waits while holding a chunk's lock, and saves never wait for an instance to be ready, so there's no deadlock:
    /// a held handle only waits for chunk locks held by calls of ready or retired instances.
    /// </para>
    /// </remarks>
    public async ValueTask<IDisposable?> LockChunkAsync(int x, int z)
    {
        var save = await this.EnterSaveAsync();
        IDisposable? chunkLock = null;

        try
        {
            chunkLock = await this.LockAsync(x, z);

            // Read once registered: an instance started after this waits for the save to be released.
            var instances = this.instances;

            foreach (var retired in instances.Retired)
                await retired.Drained;

            return instances.Running is Instance current
                ? await this.LockRunningAsync(current, save, chunkLock, x, z)
                : new Locks(save, chunkLock);
        }
        catch
        {
            chunkLock?.Dispose();
            save.Dispose();
            throw;
        }
    }

    // Adds the running instance's own lock of a chunk to the save's locks. When it can't, as its plugin is closing
    // (which stops it) or it failed, its calls are waited for instead: they may be writing into the chunk.
    private async ValueTask<IDisposable> LockRunningAsync(
        Instance current,
        IDisposable save,
        IDisposable chunkLock,
        int x,
        int z)
    {
        // Disposed already, so no call into it runs.
        if (current.TryUse() is not IDisposable use)
            return new Locks(save, chunkLock);

        try
        {
            using var call = current.Plugin.TryEnter();
            if (call is not null)
                return new Locks(save, chunkLock, use, await current.Generator.LockChunkAsync(x, z));
        }
        catch (Exception ex) when (this.Contain(current, ex))
        {
            // Retired by Contain: waited for below.
        }
        catch
        {
            use.Dispose();
            throw;
        }

        use.Dispose();
        await current.Drained;

        return new Locks(save, chunkLock);
    }

    // Registers a save handle once no newly started instance waits for the held ones.
    private async ValueTask<IDisposable> EnterSaveAsync()
    {
        while (true)
        {
            Task closed;

            lock (this.saveGate)
            {
                if (this.earlierSaves is null)
                {
                    this.saves++;
                    return new SaveHandle(this);
                }

                closed = this.earlierSaves.Task;
            }

            await closed;
        }
    }

    private void ExitSave()
    {
        TaskCompletionSource? released = null;

        lock (this.saveGate)
        {
            this.saves--;

            if (this.saves == 0)
            {
                released = this.earlierSaves;
                this.earlierSaves = null;
            }
        }

        released?.SetResult();
    }

    // Under saveGate, as an instance starts: holds new save handles back until the held ones are released, and
    // returns a task that completes when they are.
    private Task CloseSaveGate()
    {
        if (this.saves == 0)
            return Task.CompletedTask;

        this.earlierSaves ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        return this.earlierSaves.Task;
    }

    // Takes the chunk's lock to generate it, and reads the running instance (null when stopped) under it, once, so a
    // swap applies from the next chunk. A newly started instance is waited for without the chunk's lock, which the
    // saves it waits for may need.
    private async ValueTask<(IDisposable ChunkLock, Instance? Running)> LockToGenerateAsync(int x, int z)
    {
        while (true)
        {
            if (this.instances.Running is Instance starting)
                await starting.Ready;

            var chunkLock = await this.LockAsync(x, z);
            var running = this.instances.Running;

            if (running is null || running.Ready.IsCompleted)
                return (chunkLock, running);

            chunkLock.Dispose();
        }
    }

    // Contains a failure of the plugin's generator, which would otherwise end chunk management or the server: it's
    // logged, and the level stops until the plugin reloads. Only the first failure of a generator is logged.
    private bool Contain(Instance failed, Exception exception)
    {
        if (exception is OutOfMemoryException)
            return false;

        lock (transitions)
        {
            if (this.instances.Running != failed)
                return true;

            this.failedBy = failed.Plugin;
            this.Detach(failed);
        }

        Log.Failed(this.Logger, exception, this.LevelName, failed.Plugin.Info.Name);
        failed.Retire();

        return true;
    }

    // Takes the running instance out, keeping it among the retired ones until its calls end. Under transitions; the
    // caller retires it once out of the lock, as disposing it may run the plugin's code.
    private void Detach(Instance running) =>
        this.instances = new Instances(null, this.instances.Retired.Add(running));

    // Drops a retired instance once its calls ended.
    private void Forget(Instance drained)
    {
        lock (transitions)
            this.instances = this.instances with { Retired = this.instances.Retired.Remove(drained) };
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
    /// The running instance of a plugin's generator, if any, and the retired ones whose calls haven't ended.
    /// </summary>
    private sealed record Instances(Instance? Running, ImmutableArray<Instance> Retired)
    {
        public static Instances None { get; } = new(null, []);
    }

    /// <summary>
    /// An instance of a plugin's generator. It's disposed once it's retired (stopped, replaced or failed) and the
    /// uses of it ended, and holds its plugin until then.
    /// </summary>
    /// <param name="onDrained">Called once it's retired and its uses ended, before it's disposed.</param>
    private sealed class Instance(
        ILevelGenerator generator,
        PluginContainer plugin,
        PluginServiceProvider services,
        IDisposable hold,
        ILogger logger,
        Action<Instance> onDrained)
    {
        // One while it isn't retired, plus one per use.
        private int users = 1;
        private int retired;

        private readonly TaskCompletionSource drained =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ILevelGenerator Generator { get; } = generator;

        public PluginContainer Plugin { get; } = plugin;

        /// <summary>
        /// Completes once the save handles held when it started are released; until then it doesn't generate. Set
        /// before it's published.
        /// </summary>
        public Task Ready { get; set; } = Task.CompletedTask;

        /// <summary>
        /// Completes once it's retired and its uses ended: no call into it runs any more.
        /// </summary>
        public Task Drained => this.drained.Task;

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
            if (Interlocked.Decrement(ref this.users) != 0)
                return;

            onDrained(this);
            this.drained.SetResult();
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

    // A save's handle on the save gate.
    private sealed class SaveHandle(PluginLevelGenerator owner) : IDisposable
    {
        private PluginLevelGenerator? owner = owner;

        public void Dispose() => Interlocked.Exchange(ref this.owner, null)?.ExitSave();
    }

    // A save's locks: the running generator's own and its use, if any, the chunk's, then the save's handle, released
    // in that order.
    private sealed class Locks(
        IDisposable save,
        IDisposable chunkLock,
        IDisposable? use = null,
        IDisposable? generatorLock = null) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                generatorLock?.Dispose();
            }
            finally
            {
                use?.Dispose();
                chunkLock.Dispose();
                save.Dispose();
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
