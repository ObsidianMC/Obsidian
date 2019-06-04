using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    class ChunkIndirectPalette : ChunkPalette
    {
        public override Task<byte[]> ToArrayAsync() => throw new NotImplementedException();
    }
}
