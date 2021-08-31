using System;
using System.Threading.Tasks;

namespace Obsidian.API
{
    public interface IWorld
    {
        public string Name { get; }
        public bool Loaded { get; }

        public long Time { get; }
        public Gamemode GameType { get; }

        public Block? GetBlock(Vector location);
        public Block? GetBlock(int x, int y, int z);
        public void SetBlock(Vector location, Block block, bool blockUpdate = false);
        public void SetBlock(int x, int y, int z, Block block, bool blockUpdate = false);

        public Task<IEntity> SpawnEntityAsync(VectorF position, EntityType type);
        public Task SpawnExperienceOrbs(VectorF position, short count);
        public Task SpawnPainting(Vector position, Painting painting, PaintingDirection direction, Guid uuid = default);
    }
}
