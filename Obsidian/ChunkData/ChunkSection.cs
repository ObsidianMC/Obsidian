using Obsidian.Net;
using Obsidian.Util.Collection;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public class ChunkSection
    {
        public BlockStateContainer Container { get; }

        public NibbleArray BlockLightArray = new NibbleArray(16 * 16 * 16);
        public NibbleArray SkyLightArray = new NibbleArray(16 * 16 * 16);

        public bool Overworld = true;

        public int YBase { get; set; }

        public ChunkSection(byte bitsPerBlock = 4)
        {
            this.Container = new BlockStateContainer(bitsPerBlock);
        }

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

        public int GetHighestBlock(int x, int z)
        {
            for(int y = 15; y >= 0; y--)
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