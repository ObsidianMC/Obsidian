using Obsidian.Blocks;
using Obsidian.Net;
using Obsidian.Util.Collection;
using Obsidian.Util.DataTypes;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public class ChunkSection
    {
        private BlockStateContainer Container { get; }

        public NibbleArray BlockLightArray = new NibbleArray(16 * 16 * 16);
        public NibbleArray SkyLightArray = new NibbleArray(16 * 16 * 16);

        public bool Overworld = true;

        public int? YBase { get; }

        public ChunkSection(byte bitsPerBlock = 4, int? yBase = null)
        {
            this.Container = new BlockStateContainer(bitsPerBlock);
            this.YBase = YBase;
        }

        public async Task WriteToAsync(MinecraftStream stream)
        {
            await using var data = new MinecraftStream();
            await this.Container.WriteToAsync(data);

            /*await data.WriteAsync(this.BlockLightArray.Data);//Lights get sent in a different packet

            if (Overworld)
                await data.WriteAsync(this.SkyLightArray.Data);*/

            data.Position = 0;
            await data.CopyToAsync(stream);
        }

        public Block GetBlock(Position pos) => this.GetBlock((int)pos.X, (int)pos.Y, (int)pos.Z);
        public Block GetBlock(int x, int y, int z) => (Block)this.Container.Get(x, y, z);

        public void SetBlock(Position pos, BlockState blockState) => this.SetBlock((int)pos.X, (int)pos.Y, (int)pos.Z, blockState);
        public void SetBlock(int x, int y, int z, BlockState blockState) => this.Container.Set(x, y, z, blockState);

        public int GetHighestBlock(int x, int z)
        {
            for (int y = 15; y >= 0; y--)
            {
                var block = this.Container.Get(x, y, z);

                if (block != null)
                    return y;
            }

            return -1;
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
}