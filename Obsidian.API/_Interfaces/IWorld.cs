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

        public Task<IEntity> SpawnEntityAsync(PositionF position, EntityType type);
        public Task SpawnExperienceOrbs(PositionF position, short count);
        public Task SpawnPainting(Position position, Painting painting, PaintingDirection direction, Guid uuid = default);
    }
}
