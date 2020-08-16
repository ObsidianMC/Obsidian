using Obsidian.BlockData;
using Obsidian.Net;
using Obsidian.Util.Collection;
using Obsidian.Util.Registry;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public class ChunkSection
    {
        public BlockStateContainer BlockStateContainer = new BlockStateContainer();
        public NibbleArray BlockLightArray = new NibbleArray(16 * 16 * 16);
        public NibbleArray SkyLightArray = new NibbleArray(16 * 16 * 16);

        public bool Overworld = true;

        public async Task CopyToAsync(MinecraftStream stream)
        {
            await using var data = new MinecraftStream();
            await data.WriteAsync(await this.BlockStateContainer.ToArrayAsync());
            await data.WriteAsync(BlockLightArray.Data);

            if (Overworld)
                await data.WriteAsync(SkyLightArray.Data);

            data.Position = 0;

            await data.CopyToAsync(stream);
        }

        public ChunkSection FillWithLight()
        {
            for (int i = 0; i < BlockLightArray.Data.Length; i++)
                BlockLightArray.Data[i] = 255;

            for (int i = 0; i < SkyLightArray.Data.Length; i++)
                SkyLightArray.Data[i] = 255;

            return this;
        }
    }


    public class BlockStateContainer
    {
        private const byte BitsPerEntry = 14;
        private DataArray BlockStorage = new DataArray(BitsPerEntry);

        public void Set(int x, int y, int z, BlockState blockState)
        {
            this.BlockStorage[GetIndex(x, y, z)] = blockState.Id;
        }

        public BlockState Get(int x, int y, int z)
        {
            int storageId = this.BlockStorage[GetIndex(x, y, z)];
            foreach (var blockState in BlockRegistry.BLOCK_STATES.Values)
            {
                if (blockState.Id == storageId)
                    return blockState;
            }
            return null;
        }

        private int GetIndex(int x, int y, int z) => ((y * 16) + z) * 16 + x;

        public async Task<byte[]> ToArrayAsync()
        {
            await using var stream = new MinecraftStream();
            await stream.WriteByteAsync((sbyte)BitsPerEntry);

            await stream.WriteVarIntAsync(this.BlockStorage.Storage.Length);
            await stream.WriteLongArrayAsync(this.BlockStorage.Storage);

            return stream.ToArray();
        }
    }


}