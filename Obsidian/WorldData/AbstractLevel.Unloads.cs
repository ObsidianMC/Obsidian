using System.Threading;

namespace Obsidian.WorldData;

// Chunk and region unloads, like vanilla's chunk map: under the simulation gate, one step takes each chunk's final save
// and takes the chunk out of the level (see Region.DetachForUnloadAsync), so no tick, block entity or entity reaches it
// afterwards. The save becomes a pending write (see PendingChunkWrites), which loads read until it's committed;
// compressing, writing and syncing it happen outside the gate.
public abstract partial class AbstractLevel
{
    // Saves and unloads own this through their final writes, syncs and removal. Always acquired before
    // simulationGate: an autosave must never hold that gate waiting for an unload's writes, while the unload needs the
    // gate for its snapshot. It also keeps a region open until an autosave syncs it.
    private readonly SemaphoreSlim unloadGate = new(1, 1);

    /// <summary>
    /// The final saves of the chunks the level unloaded that aren't committed yet, shared by its regions, so a load
    /// never reads an older version from the files.
    /// </summary>
    internal PendingChunkWrites PendingWrites { get; } = new();

    // Regions removed from the level that are still writing their final saves and closing their files, by position,
    // until they're closed. A region object loaded at the same position meanwhile opens the files once this ends (see
    // the Region constructor). Under regionLock.
    private readonly Dictionary<long, Region> closingRegions = [];

    private async Task UnloadManagedChunkAsync(long chunk)
    {
        await this.unloadGate.WaitAsync();
        try
        {
            if (this.IsDisposed)
                return;

            NumericsHelper.LongToInts(chunk, out var x, out var z);
            var local = (NumericsHelper.Modulo(x, Region.CubicRegionSize), NumericsHelper.Modulo(z, Region.CubicRegionSize));
            Region? region = null;
            Region.UnloadedChunks? unloaded = null;

            await this.simulationGate.WaitAsync();
            try
            {
                // Tickets and travel pins may have arrived since management collected the candidates. The closure
                // only has to last until the chunk is gone: then no entity can leave it or arrive in it.
                if (!this.TryReleaseChunkClosedToTravel(chunk))
                    return;

                try
                {
                    // The chunk's final save has its block entities' changes made since the last tick.
                    this.TakeBlockEntityChanges();

                    var current = this.GetRegionForChunk(x, z);
                    if (current is Region concrete)
                    {
                        region = concrete;
                        unloaded = await concrete.DetachForUnloadAsync([local]);
                    }
                    else if (current is not null)
                    {
                        // A custom region has no snapshot API, so its whole save stays under the gate.
                        await current.UnloadChunk(local.Item1, local.Item2);
                    }
                }
                finally
                {
                    this.ReopenChunkToTravel(chunk);
                }
            }
            finally
            {
                this.simulationGate.Release();
            }

            if (region is not null && unloaded is not null)
                await region.WriteUnloadedAsync(unloaded);
        }
        finally
        {
            this.unloadGate.Release();
        }
    }

    public async Task UnloadRegionAsync(int regionX, int regionZ)
    {
        await this.unloadGate.WaitAsync();
        try
        {
            if (!this.IsDisposed)
                await this.UnloadRegionCoreAsync(regionX, regionZ);
        }
        finally
        {
            this.unloadGate.Release();
        }
    }

    // The caller holds unloadGate.
    private async Task UnloadRegionCoreAsync(int regionX, int regionZ)
    {
        var key = NumericsHelper.IntsToLong(regionX, regionZ);

        // A region whose files fail to open can't be saved, so it's left as it is rather than retired. Waited for
        // outside the gate, since a region loaded where another is closing opens its files once that one closed them.
        if (this.Regions.TryGetValue(key, out var opening) && opening is Region initializing)
            await initializing.InitAsync();

        IRegion? removed;
        Region.UnloadedChunks? unloaded = null;
        var kept = false;

        await this.simulationGate.WaitAsync();
        try
        {
            this.TakeBlockEntityChanges();
            lock (this.Fluids.TickLock)
            {
                if (!this.Regions.TryGetValue(key, out removed))
                    return;

                if (this.HasRegionTickets(regionX, regionZ))
                    return;

                // Not while an entity that left it for another level may still come back or be kept there.
                if (removed is Region leaving && !this.TryCloseRegionToTravel(leaving))
                    return;

                (removed as Region)?.Retire();
            }

            // The region stops taking lookups and admissions in the same step as its final snapshot: a lookup from
            // then on loads a fresh region object, which reads the pending writes until it can open the files.
            if (removed is Region region)
            {
                var positions = region.ResidentChunks()
                    .Select(chunk => (NumericsHelper.Modulo(chunk.X, Region.CubicRegionSize),
                        NumericsHelper.Modulo(chunk.Z, Region.CubicRegionSize)))
                    .ToArray();
                try
                {
                    unloaded = await region.DetachForUnloadAsync(positions);
                }
                catch
                {
                    this.KeepRegion(region);
                    throw;
                }

                // A chunk whose final snapshot failed is still in the region, so the level keeps the region: its next
                // save writes the chunk, and a later unload takes it. The chunks taken out are written below.
                kept = unloaded.Failures.Count != 0;
                if (kept)
                    this.KeepRegion(region);
            }
            else
            {
                // A custom region has no snapshot API, so its whole save stays under the gate.
                await removed.FlushAsync();
            }

            if (!kept)
                this.RemoveUnloadedRegion(key, removed);
        }
        finally
        {
            this.simulationGate.Release();
        }

        if (removed is not Region written)
        {
            await removed.DisposeAsync();
            return;
        }

        // Throws the failed snapshots once the others are written.
        if (kept)
        {
            await written.WriteUnloadedAsync(unloaded!);
            return;
        }

        try
        {
            await written.WriteUnloadedAsync(unloaded!);
            await written.SyncToDiskAsync();
        }
        finally
        {
            await this.CloseRemovedRegionAsync(written, key);
        }
    }

    // Takes the region out of the level in the step that took its final snapshot. Call under the simulation gate.
    private void RemoveUnloadedRegion(long key, IRegion removed)
    {
        using (this.regionLock.EnterScope())
        {
            if (removed is Region closing)
                this.closingRegions[key] = closing;

            lock (this.Fluids.TickLock)
            {
                if (this.Regions.TryRemove(KeyValuePair.Create(key, removed)))
                    Interlocked.Increment(ref this.regionsVersion);
            }
        }

        // The snapshot took the entities it saves out of the level; any left (such as those in chunks never loaded)
        // go with the region, so they leave the entity index too.
        foreach (var (_, entity) in removed.Entities)
            EntityGrid.Leave(entity);
    }

    // Keeps a region whose final snapshot failed (see Region.Unretire), open to travel again. Call under the
    // simulation gate.
    private void KeepRegion(Region region)
    {
        region.Unretire();
        using (this.TransferLock.Enter())
            region.TransfersClosed = false;
    }

    // Closes the files of a removed region, then lets a region object loaded at its position open them. Saves that
    // failed stay pending, and the next save retries them through that region object.
    private async Task CloseRemovedRegionAsync(Region region, long key)
    {
        try
        {
            await region.DisposeAsync();
        }
        finally
        {
            region.EndRegionUnload();

            using (this.regionLock.EnterScope())
            {
                if (this.closingRegions.TryGetValue(key, out var closing) && ReferenceEquals(closing, region))
                    this.closingRegions.Remove(key);
            }
        }
    }

    /// <summary>
    /// Writes again the unloaded chunks whose final writes failed (see <see cref="PendingChunkWrites.Failed"/>),
    /// through the region object at their position now, as a load of one would read it. A failure is added to
    /// <paramref name="failures"/>, and the write stays pending for the next save.
    /// </summary>
    private async Task RetryFailedUnloadWritesAsync(List<Exception> failures)
    {
        var byRegion = this.PendingWrites.Failed().GroupBy(write => (
            write.ChunkX >> Region.CubicRegionSizeShift,
            write.ChunkZ >> Region.CubicRegionSizeShift));

        foreach (var writes in byRegion)
        {
            var (regionX, regionZ) = writes.Key;
            Region? region;
            try
            {
                region = this.LoadRegion(regionX, regionZ) as Region;
            }
            catch (Exception opening)
            {
                // Its files can't be opened now: its writes stay pending for the next save, and the other regions
                // are saved all the same.
                failures.Add(new InvalidOperationException($"Could not save region ({regionX}, {regionZ}).", opening));
                continue;
            }

            if (region is null)
                continue;

            var failure = await CaptureRegionSaveFailureAsync(region, () => region.RetryPendingWritesAsync(writes));
            if (failure is not null)
                failures.Add(failure);
        }
    }
}
