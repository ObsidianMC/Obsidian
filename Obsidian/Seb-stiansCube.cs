namespace Obsidian
{
    public class SebastiansCube
    {
        private short shared = 0;
        private short[] blocks;

        private byte offsetX, offsetY, offsetZ;

        internal const int width = 16;
        internal const int height = 16;

        private const int xMult = (width * width * height) / width;
        private const int zMult = (width * width * height) / (width * width);

        public SebastiansCube(int offsetX, int offsetY, int offsetZ)
        {
            this.offsetX = (byte)offsetX;
            this.offsetY = (byte)offsetY;
            this.offsetZ = (byte)offsetZ;
        }

        public short this[int x, int y, int z]
        {
            get
            {
                if (shared != short.MinValue)
                    return shared;

                return (blocks ??= new short[width * width * height])[(x - offsetX) * xMult + (z - offsetZ) * zMult + (y - offsetY)];
            }

            set
            {
                if (shared != short.MinValue)
                {
                    if (value == shared)
                    {
                        return;
                    }
                    else
                    {
                        shared = short.MinValue;
                        blocks = new short[width * width * height];
                    }
                }
                blocks[(x - offsetX) * xMult + (z - offsetZ) * zMult + (y - offsetY)] = value;
            }
        }
    }
}
