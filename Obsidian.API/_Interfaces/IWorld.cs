using System.Threading.Tasks;

namespace Obsidian.API
{
    public interface IWorld
    {
        public string Name { get; }
        public bool Loaded { get; }

        public long Time { get; }
        public Gamemode GameType { get; }

        public Task SpawnExperienceOrb(PositionF position, short count);
    }
}
