using Obsidian.BlockData;
using Obsidian.Net;
using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public class ChunkSection : ISerializable
    {
        public BlockStateContainer BlockStateContainer = new BlockStateContainer();
        public NibbleArray BlockLightArray = new NibbleArray(16 * 16 * 16);
        public NibbleArray SkyLightArray = new NibbleArray(16 * 16 * 16);

        public bool Overworld = true;//TODO

        public async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteAsync(await BlockStateContainer.ToArrayAsync());
                await stream.WriteAsync(BlockLightArray.Data);

                if (Overworld)
                {
                    await stream.WriteAsync(SkyLightArray.Data);
                }

                return stream.ToArray();
            }
        }
    }

    public class BlockStateContainer
    {
        private const byte BitsPerEntry = 14;//USING GLOBAL PALETTE FOR NOW

        private DataArray BlockStorage = new DataArray(BitsPerEntry);

        public void Set(int x, int y, int z, BlockState blockState)
        {
            this.BlockStorage[GetIndex(x, y, z)] = blockState.Id;
        }

        public void Set(double x, double y, double z, BlockState blockState)
        {
            this.BlockStorage[GetIndex((int)x, (int)y, (int)z)] = blockState.Id;
        }

        public BlockState Get(int x, int y, int z)
        {
            int storageId = this.BlockStorage[GetIndex(x, y, z)];
            foreach (var blockState in Blocks.BLOCK_STATES)
            {
                if (blockState.Id == storageId)
                    return blockState;
            }
            return null;
        }

        private int GetIndex(int x, int y, int z) => ((y * 16) + z) * 16 + x;

        public async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteByteAsync((sbyte)BitsPerEntry);

                await stream.WriteVarIntAsync(this.BlockStorage.Storage.Length);
                await stream.WriteLongArrayAsync(this.BlockStorage.Storage);

                return stream.ToArray();
            }
        }
    }

    

}
