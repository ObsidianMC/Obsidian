using System.Threading;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    // Saves and unloads own this through their final writes, syncs and removal. Always acquired before
    // simulationGate: an autosave must never hold that gate waiting for an unload's flushLock or write turns,
    // while the unload needs the gate for its next snapshot. It also keeps a region open until an autosave syncs it.
    private readonly SemaphoreSlim unloadGate = new(1, 1);

    private async Task<bool> TakeUnloadSnapshotAsync(Func<Task<bool>> snapshot)
    {
        await this.simulationGate.WaitAsync();
        try
        {
            this.TakeBlockEntityChanges();
            return await snapshot();
        }
        finally
        {
            this.simulationGate.Release();
        }
    }

    // Chunk management keeps its travel closure through the entire write, including retries of metadata changes.
    private async Task UnloadManagedChunkAsync(long chunk)
    {
        await this.unloadGate.WaitAsync();
        var closed = false;
        try
        {
            if (this.IsDisposed)
                return;

            IRegion? region;
            NumericsHelper.LongToInts(chunk, out var x, out var z);
            await this.simulationGate.WaitAsync();
            try
            {
                // Tickets and travel pins may have arrived since management collected the candidates.
                closed = this.TryReleaseChunkClosedToTravel(chunk);
                if (!closed)
                    return;

                region = this.GetRegionForChunk(x, z);
            }
            finally
            {
                this.simulationGate.Release();
            }

            var localX = NumericsHelper.Modulo(x, Region.CubicRegionSize);
            var localZ = NumericsHelper.Modulo(z, Region.CubicRegionSize);
            if (region is Region concrete)
            {
                await concrete.UnloadChunkAsync(localX, localZ, this.TakeUnloadSnapshotAsync);
            }
            else if (region is not null)
            {
                await this.simulationGate.WaitAsync();
                try
                {
                    this.TakeBlockEntityChanges();
                    await region.UnloadChunk(localX, localZ);
                }
                finally
                {
                    this.simulationGate.Release();
                }
            }
        }
        finally
        {
            if (closed)
                this.ReopenChunkToTravel(chunk);

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

    // The caller holds unloadGate. Autosave retries failed unloads through this same final-save lifecycle.
    private async Task UnloadRegionCoreAsync(int regionX, int regionZ, bool retry = false)
    {
        var value = NumericsHelper.IntsToLong(regionX, regionZ);
        IRegion? r;
        await this.simulationGate.WaitAsync();
        try
        {
            this.TakeBlockEntityChanges();
            lock (this.Fluids.TickLock)
            {
                if (!Regions.TryGetValue(value, out r))
                    return;

                if (!retry && this.HasRegionTickets(regionX, regionZ))
                    return;

                // Not while an entity that left it for another level may still come back or be kept there.
                if (!retry && r is Region leaving && !this.TryCloseRegionToTravel(leaving))
                    return;

                // A failed unload stays registered until autosave retries it. Once retired it must finish closing,
                // even if a ticket arrived meanwhile; readers then acquire an ordinary fresh region.
                if (r is Region concrete)
                    concrete.RetireLighting();
            }
        }
        finally
        {
            this.simulationGate.Release();
        }

        try
        {
            while (true)
            {
                lock (this.Fluids.TickLock)
                {
                    if (r is Region { StructureChangesSealed: true })
                        break;
                }

                if (r is Region savingRegion)
                {
                    await savingRegion.WriteChangesAsync(snapshotUnload: this.TakeUnloadSnapshotAsync);
                    await savingRegion.SyncToDiskAsync();
                }
                else
                {
                    // A custom region has no snapshot API, so its save still needs the simulation gate.
                    await this.simulationGate.WaitAsync();
                    try
                    {
                        this.TakeBlockEntityChanges();
                        await r.FlushAsync();
                    }
                    finally
                    {
                        this.simulationGate.Release();
                    }
                }

                lock (this.Fluids.TickLock)
                {
                    if (r is Region saving && !saving.TrySealStructureChanges())
                        continue;

                    break;
                }
            }

            await r.DisposeAsync();
            lock (this.Fluids.TickLock)
            {
                if (Regions.TryRemove(KeyValuePair.Create(value, r)))
                    Interlocked.Increment(ref this.regionsVersion);
            }

            (r as Region)?.EndRegionUnload();

            // The flush takes out the entities it saves; any it left (such as those in chunks never generated)
            // go with the region, so they leave the entity index too.
            foreach (var (_, entity) in r.Entities)
                EntityGrid.Leave(entity);
        }
        catch (Exception failure)
        {
            (r as Region)?.EndRegionUnload(failure);
            throw;
        }
    }
}
