using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public abstract class ChunkPalette : ISerializable
    {
        public abstract byte BitsPerBlock { get; set; }

        public abstract Task<byte[]> ToArrayAsync();
        
    }
}
