using System.Threading.Tasks;
using Obsidian.World;

namespace Obsidian.Net.Packets.Play
{
    public class ServerDifficulty
    {
        public ServerDifficulty(Difficulty difficulty) => this.Difficulty = difficulty;

        public ServerDifficulty(byte difficulty) => this.difficulty = difficulty;

        private byte difficulty;

        public Difficulty Difficulty
        {
            get => (Difficulty)difficulty;
            set => difficulty = (byte)(int)value;
        }

        public static async Task<ServerDifficulty> FromArrayAsync(byte[] data) => new ServerDifficulty(await new MinecraftStream(data).ReadUnsignedByteAsync());

        public async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteUnsignedByteAsync(this.difficulty);
                return stream.ToArray();
            }
        }
    }
}