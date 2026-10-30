using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators.Density;

namespace Obsidian.WorldData.Generators.Terrain;

internal sealed partial class MaterialRules
{
    /// <summary>The column depths and cached samples visible to a material rule evaluation.</summary>
    internal sealed class MaterialContext
    {
        private readonly MaterialRules owner;
        private readonly DensityVolume volume;
        private readonly Dictionary<DensityFunction, float[]> densities = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<NoiseStack, float> columnNoises = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<NoiseStack, float> blockNoises = new(ReferenceEqualityComparer.Instance);
        private float[]? surfaceLevels;
        private string? biome;
        private int localX;
        private int localZ;

        public MaterialContext(MaterialRules owner, TerrainContext terrain, DensityVolume? volume = null)
        {
            this.owner = owner;
            this.Terrain = terrain;

            if (volume is DensityVolume supplied)
            {
                this.volume = supplied;
                return;
            }

            var highest = terrain.Volume.MinBlockY;

            for (var x = 0; x < 16; x++)
            {
                for (var z = 0; z < 16; z++)
                {
                    highest = Math.Max(highest, SurfaceHeight(terrain.Chunk, x, z) - 1);
                }
            }

            // Vanilla's material volume ends at the top of the highest nonempty section, before extensions.
            var sectionTop = (highest & ~15) + 15;
            this.volume = terrain.Volume with { SizeY = Math.Max(1, sectionTop - terrain.Volume.MinBlockY + 1) };
        }

        public TerrainContext Terrain { get; }

        public int X { get; private set; }

        public int Y { get; private set; }

        public int Z { get; private set; }

        public int Above { get; private set; }

        public int Below { get; private set; }

        public int Water { get; private set; }

        public int SurfaceDepth { get; private set; }

        public int GradientX { get; private set; }

        public int GradientZ { get; private set; }

        public string Biome => this.biome ??= this.Terrain.BiomeAt(this.X, this.Y, this.Z);

        public double Secondary => this.Noise(this.owner.generator.Compiler.Noise("minecraft:surface_secondary"), false);

        public int MinSurface
        {
            get
            {
                var plane = new DensityVolume(
                    this.volume.SizeX,
                    1,
                    this.volume.SizeZ,
                    this.volume.MinBlockX,
                    0,
                    this.volume.MinBlockZ);

                if (this.surfaceLevels is null)
                {
                    this.surfaceLevels = new float[plane.Size];
                    this.owner.generator.Router.Functions["chunk_surface_level"].SampleVolume(this.surfaceLevels, plane);
                }

                var index = plane.Index(this.X - plane.MinBlockX, 0, this.Z - plane.MinBlockZ);
                var level = (int)MathF.Floor(this.surfaceLevels[index]);

                return level + this.SurfaceDepth - 8;
            }
        }

        public void UpdateColumn(int x, int z)
        {
            this.localX = x;
            this.localZ = z;
            this.X = this.Terrain.Volume.BlockX(x);
            this.Z = this.Terrain.Volume.BlockZ(z);
            this.columnNoises.Clear();
            var compiler = this.owner.generator.Compiler;
            var random = new PositionalRandom(compiler.Random, this.X, 0, this.Z);
            double noise = compiler.Noise("minecraft:surface").Get(this.X, 0.0, this.Z);
            this.SurfaceDepth = (int)(noise * 2.75 + 3.0 + random.NextDouble() * 0.25);

            var chunk = this.Terrain.Chunk;
            this.GradientX = SurfaceHeight(chunk, Math.Min(x + 1, 15), z)
                - SurfaceHeight(chunk, Math.Max(x - 1, 0), z);
            this.GradientZ = SurfaceHeight(chunk, x, Math.Min(z + 1, 15))
                - SurfaceHeight(chunk, x, Math.Max(z - 1, 0));
        }

        public void UpdateBlock(int y, int above, int below, int water)
        {
            this.Y = y;
            this.Above = above;
            this.Below = below;
            this.Water = water;
            this.biome = null;
            this.blockNoises.Clear();
        }

        public float Noise(NoiseStack noise, bool is3d)
        {
            var cache = is3d ? this.blockNoises : this.columnNoises;

            if (!cache.TryGetValue(noise, out var value))
            {
                value = noise.Get(this.X, is3d ? this.Y : 0.0, this.Z);
                cache.Add(noise, value);
            }

            return value;
        }

        public float Density(DensityFunction function, bool volume)
        {
            if (!volume)
            {
                return function.Sample(this.X, this.Y, this.Z);
            }

            var x = this.X - this.volume.MinBlockX;
            var y = this.Y - this.volume.MinBlockY;
            var z = this.Z - this.volume.MinBlockZ;
            var inside = (uint)x < this.volume.SizeX && (uint)y < this.volume.SizeY && (uint)z < this.volume.SizeZ;

            if (!inside)
            {
                return function.Sample(this.X, this.Y, this.Z);
            }

            if (!this.densities.TryGetValue(function, out var values))
            {
                values = new float[this.volume.Size];
                function.SampleVolume(values, this.volume);
                this.densities.Add(function, values);
            }

            return values[this.volume.Index(x, y, z)];
        }

        public IBlock GetBlock(int y)
        {
            var chunk = this.Terrain.Chunk;

            if (y < chunk.MinY || y >= chunk.MinY + chunk.Height)
            {
                return BlocksRegistry.Air;
            }

            return chunk.GetBlock(this.localX, y, this.localZ);
        }

        public void SetBlock(int y, IBlock block)
        {
            var chunk = this.Terrain.Chunk;

            if (y < chunk.MinY || y >= chunk.MinY + chunk.Height)
            {
                return;
            }

            chunk.SetBlock(this.localX, y, this.localZ, block);
            UpdateHeight(chunk, HeightmapType.WorldSurfaceWG, this.localX, y, this.localZ, block);
            UpdateHeight(chunk, HeightmapType.OceanFloorWG, this.localX, y, this.localZ, block);

            // Vanilla's material column marks every state with a fluid for post-processing, such as the water of
            // swamp rules. Unlike density fill, it doesn't consult the aquifer's scheduling decision.
            if (block.HasFluid())
            {
                this.Terrain.MarkForPostProcessing(this.X, y, this.Z);
            }
        }
    }
}
