using Obsidian.Util;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public abstract class ChunkPalette : ISerializable
    {
        public abstract Task<byte[]> ToArrayAsync();

        public abstract byte BitsPerBlock { get; set; }
    }
}
