using Obsidian.BlockData;
using Obsidian.Net;
using Obsidian.Util.Collection;
using Obsidian.Util.Registry;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public class ChunkSection
    {
        public BlockStateContainer Container = new BlockStateContainer();

        public NibbleArray BlockLightArray = new NibbleArray(16 * 16 * 16);
        public NibbleArray SkyLightArray = new NibbleArray(16 * 16 * 16);

        public bool Overworld = true;

        public async Task WriteToAsync(MinecraftStream stream)
        {
            await using var data = new MinecraftStream();
            await this.Container.WriteToAsync(data);

            await data.WriteAsync(this.BlockLightArray.Data);

            if (Overworld)
                await data.WriteAsync(this.SkyLightArray.Data);

            data.Position = 0;
            await data.CopyToAsync(stream);
        }

        public ChunkSection FillWithLight()
        {
            for (int i = 0; i < this.BlockLightArray.Data.Length; i++)
                this.BlockLightArray.Data[i] = 255;

            for (int i = 0; i < this.SkyLightArray.Data.Length; i++)
                this.SkyLightArray.Data[i] = 255;

            return this;
        }
    }


    public class BlockStateContainer
    {
        private const byte BitsPerEntry = 14;

        private readonly DataArray BlockStorage = new DataArray(BitsPerEntry);

        private IBlockStatePalette Palette { get; }

        public void Set(int x, int y, int z, BlockState blockState) => this.BlockStorage[GetIndex(x, y, z)] = blockState.Id;

        public void Set(double x, double y, double z, BlockState blockState) => this.BlockStorage[GetIndex((int)x, (int)y, (int)z)] = blockState.Id;

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

        public async Task WriteToAsync(MinecraftStream stream)
        {
            await stream.WriteByteAsync((sbyte)BitsPerEntry);

            await stream.WriteVarIntAsync(this.BlockStorage.Storage.Length);
            await stream.WriteLongArrayAsync(this.BlockStorage.Storage);
        }
    }


}