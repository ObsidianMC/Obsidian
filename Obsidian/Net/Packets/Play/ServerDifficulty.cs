using Obsidian.Util;

namespace Obsidian.Net.Packets
{
    public class ServerDifficulty
    {
        public ServerDifficulty(Difficulty difficulty) => this.Difficulty = difficulty;

        public ServerDifficulty(byte difficulty) => this.difficulty = difficulty;

        [Variable]
        private byte difficulty;

        public Difficulty Difficulty
        {
            get => (Difficulty)difficulty;
            set => difficulty = (byte)(int)value;
        }
    }
}