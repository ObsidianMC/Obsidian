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
    }
}
