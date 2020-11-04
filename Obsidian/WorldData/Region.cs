using Obsidian.Entities;
using Obsidian.Util.Collection;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.WorldData
{
    public class Region
    {
        public const int CUBIC_REGION_SIZE = 10;

        private bool cancel = false;
        public int X { get; }
        public int Z { get; }

        public ConcurrentDictionary<int, Entity> Entities { get; private set; } = new ConcurrentDictionary<int, Entity>();

        public DenseCollection<Chunk> LoadedChunks { get; private set; } = new DenseCollection<Chunk>(CUBIC_REGION_SIZE, CUBIC_REGION_SIZE);

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
