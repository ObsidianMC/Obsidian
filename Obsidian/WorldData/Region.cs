using Obsidian.Concurrency;
using Obsidian.Entities;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.WorldData
{
    public class Region
    {
        private bool cancel = false;
        public int X { get; }
        public int Z { get; }

        public ConcurrentDictionary<int, Entity> Entities { get; private set; } = new ConcurrentDictionary<int, Entity>();

        public ConcurrentHashSet<Chunk> LoadedChunks { get; private set; } = new ConcurrentHashSet<Chunk>();

        internal Region(int x, int z)
        {
            this.X = x;
            this.Z = z;
        }

        internal async Task BeginTickAsync(CancellationToken cts)
        {
            while (!cts.IsCancellationRequested || cancel)
            {
                await Task.Delay(20);

                foreach (var (_, entity) in this.Entities)
                    await entity.TickAsync();
            }
        }

        internal void Cancel() => this.cancel = true;
    }
}
