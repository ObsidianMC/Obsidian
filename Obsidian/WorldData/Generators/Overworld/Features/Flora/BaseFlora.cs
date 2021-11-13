using Obsidian.API;
using System;
using System.Collections.Generic;

namespace Obsidian.WorldData.Generators.Overworld.Features.Flora
{
    public abstract class BaseFlora
    {
        protected readonly World world;

        protected Material FloraMat { get; set; }

        protected List<Material> ValidSourceBlocks = new()
        {
            Material.GrassBlock,
            Material.Dirt,
            Material.Podzol
        };

        protected BaseFlora(World world, Material mat = Material.RedTulip)
        {
            this.world = world;
            this.FloraMat = mat;
        }

        public virtual void GenerateFlora(Vector origin, int seed, int radius, int density)
        {
            density = Math.Max(1, 10 - density);
            var seedRand = new Random(seed + origin.GetHashCode());

            for (int rz = 0; rz <= radius * 2; rz++)
            {
                for (int rx = 0; rx <= radius * 2; rx++)
                {
                    if ((radius - rx) * (radius - rx) + (radius - rz) * (radius - rz) <= (radius * radius))
                    {
                        int x = origin.X - radius + rx;
                        int z = origin.Z - radius + rz;
                        int y = world.GetWorldSurfaceHeight(x, z) ?? -1;
                        if (y == -1) { continue; }
                        bool isFlora = seedRand.Next(10) % density == 0;
                        var placeVec = new Vector(x, y + 1, z);
                        if (isFlora & CanPlace(placeVec))
                        {
                            world.SetBlockUntracked(placeVec, new Block(FloraMat));
                        }
                    }
                }
            }
        }

        public virtual bool CanPlace(Vector loc)
        {
            var surfaceBlock = world.GetBlock(loc + Vector.Down);
            var self = world.GetBlock(loc);
            if (surfaceBlock is null || self is null) { return false; }
            bool surfaceValid = ValidSourceBlocks.Contains(((Block)surfaceBlock).Material);
            return surfaceValid & ((Block)self).IsAir;
        }
    }
}
