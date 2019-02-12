using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets
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

        public static async Task<ServerDifficulty> FromArrayAsync(byte[] data) => new ServerDifficulty(await new MemoryStream(data).ReadUnsignedByteAsync());

        public async Task<byte[]> ToArrayAsync()
        {
            MemoryStream stream = new MemoryStream();
            await stream.WriteUnsignedByteAsync(this.difficulty);
            return stream.ToArray();
        }
    }
}