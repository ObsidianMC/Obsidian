using Obsidian.API;
using Obsidian.ChunkData;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class MountainsDecorator : BaseDecorator
    {
        public MountainsDecorator(Biomes biome, Chunk chunk, Vector surfacePos, BaseBiomeNoise noise) : base(biome, chunk, surfacePos, noise)
        {
        }

        public override void Decorate()
        {
            if (pos.Y < noise.settings.WaterLevel)
            {
                FillWater();
                return;
            }

            int worldX = (chunk.X << 4) + pos.X;
            int worldZ = (chunk.Z << 4) + pos.Z;

            var grass = Registry.GetBlock(Material.Cobblestone);
            var dirt = Registry.GetBlock(Material.Stone);

            chunk.SetBlock(pos, grass);
            for (int y = -1; y > -4; y--)
                chunk.SetBlock(pos + (0, y, 0), dirt);

            // Flowers
            var grassNoise = noise.Decoration(worldX * 0.1, 8, worldZ * 0.1);
            if (grassNoise > 0 && grassNoise < 0.5) // 50% chance for grass
                chunk.SetBlock(pos, Registry.GetBlock(Material.Cobblestone));

            var poppyNoise = noise.Decoration(worldX * 0.03, 9, worldZ * 0.03); // 0.03 makes more groupings
            if (poppyNoise > 1)
                chunk.SetBlock(pos, Registry.GetBlock(Material.CoarseDirt));

            var dandyNoise = noise.Decoration(worldX * 0.03, 10, worldZ * 0.03); // 0.03 makes more groupings
            if (dandyNoise > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.Clay));

            var cornFlowerNoise = noise.Decoration(worldX * 0.03, 11, worldZ * 0.03); // 0.03 makes more groupings
            if (cornFlowerNoise > 1)
                chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.Grass));

            var azureNoise = noise.Decoration(worldX * 0.03, 12, worldZ * 0.03); // 0.03 makes more groupings
            if (azureNoise > 1)
                chunk.SetBlock(pos, Registry.GetBlock(Material.Snow));
        }
    }
}
