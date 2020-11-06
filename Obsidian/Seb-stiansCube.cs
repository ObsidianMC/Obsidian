using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Obsidian
{
    public class SebastiansCube
    {
        private static bool initialized;
        private static Stack<short[]> cubesCache;
        private const int cacheLimit = 125; // about 5MB
        
        private short shared = 0;
        private short[] blocks;

        private readonly byte offsetX;
        private readonly byte offsetY;
        private readonly byte offsetZ;

        internal const int width = 16;
        internal const int height = 16;
        private const int totalBlocks = width * width * height;

        private const int xMult = totalBlocks / width;
        private const int zMult = totalBlocks / (width * width);

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

                return blocks[ComputeIndex(x, y, z)];
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
                        blocks = GetBlocksArray();
                    }
                }
                blocks[ComputeIndex(x, y, z)] = value;
            }
        }

        public void Fill(short value)
        {
            if (shared != short.MinValue)
            {
                shared = value;
                return;
            }
            
            for (int i = 0; i < totalBlocks; i++)
            {
                blocks[i] = 0;
            }
            Cache();
            shared = value;
        }

        public void Fill(int x1, int y1, int z1, int x2, int y2, int z2, short value)
        {
            if (shared != short.MinValue)
            {
                if (shared != value)
                {
                    blocks = GetBlocksArray();
                }
                else
                {
                    return;
                }
            }

            Order(ref x1, ref x2);
            Order(ref y1, ref y2);
            Order(ref z1, ref z2);

            int topX = (x2 - offsetX) * xMult;
            int topY = (y2 - offsetY);
            int topZ = (z2 - offsetZ) * zMult;

            int bottomZ = (z1 - offsetZ) * zMult;
            int bottomY = y1 - offsetY;

            for (int x = (x1 - offsetX) * xMult; x <= topX; x += xMult)
            {
                for (int z = bottomZ; z <= topZ; z += zMult)
                {
                    for (int y = bottomY; y <= topY; y++)
                    {
                        blocks[x + y + z] = value;
                    }
                }
            }
        }

        public void CheckHomogeneity()
        {
            if (shared != short.MinValue)
                return;

            short id = blocks[0];
            for (int i = 1; i < totalBlocks; i++)
            {
                if (id != blocks[i])
                    return;
            }

            for (int i = 0; i < totalBlocks; i++)
            {
                blocks[i] = 0;
            }
            Cache();
            shared = id;
        }

        public static void Initialize()
        {
            if (initialized)
                return;
            initialized = true;
            
            cubesCache = new Stack<short[]>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Order(ref int a, ref int b)
        {
            if (b < a)
            {
                int temp = b;
                b = a;
                a = temp;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ComputeIndex(int x, int y, int z)
        {
            return (x - offsetX) * xMult + (z - offsetZ) * zMult + (y - offsetY);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private short[] GetBlocksArray()
        {
            return cubesCache.TryPop(out var array) ? array : new short[totalBlocks];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Cache()
        {
            if (cacheLimit > cubesCache.Count)
            {
                cubesCache.Push(blocks);
            }
            blocks = null;
        }
    }
}
