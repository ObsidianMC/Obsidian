using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators.Density;
using System.Buffers;

namespace Obsidian.WorldData.Generators.Terrain;

internal sealed partial class MaterialRules
{
    /// <summary>
    /// The column depths and cached samples visible to a material rule evaluation. Disposing it returns its density
    /// buffers to the pool.
    /// </summary>
    /// <remarks>
    /// Noises and densities are cached by the slot their rules were given when compiled (see
    /// <see cref="NoiseSlot"/> and <see cref="DensitySlot"/>), in arrays that grow for rules compiled after the
    /// context. A cached value is current while its stamp matches the column's or block's, so moving on clears
    /// nothing.
    /// </remarks>
    internal sealed class MaterialContext : IDisposable
    {
        private readonly MaterialRules owner;
        private readonly Func<int, int, int, string> biomeAt;
        private readonly DensityVolume volume;
        private float[]?[] densities;
        private float[] columnNoises;
        private int[] columnNoiseStamps;
        private float[] blockNoises;
        private int[] blockNoiseStamps;
        private int columnStamp;
        private int blockStamp;
        private float[]? surfaceLevels;
        private string? biome;
        private int? biomeId;
        private int? minSurface;

        // The block's index in the material volume, or -1 outside it, where densities are sampled at the point.
        private int volumeIndex = -1;

        // Which biome conditions' biomes the block lookups can return, by their number, when known.
        private readonly bool[]? reachableIds;

        // The biome of every block lookup, when the chunk reaches only one.
        private readonly string? onlyBiome;

        // The last biome numbered, by reference: neighboring blocks mostly share their biome's name instance.
        private string? lastNumberedBiome;
        private int lastBiomeId;

        /// <param name="biomeAt">The block biomes the rules read (see <see cref="TerrainBiomes"/>).</param>
        /// <param name="biomeNames">Every biome <paramref name="biomeAt"/> returns, when known.</param>
        public MaterialContext(
            MaterialRules owner,
            TerrainContext terrain,
            Func<int, int, int, string> biomeAt,
            DensityVolume? volume = null,
            IReadOnlyList<string>? biomeNames = null)
        {
            this.owner = owner;
            this.Terrain = terrain;
            this.biomeAt = biomeAt;
            this.densities = new float[]?[owner.densitySlots.Count];
            this.columnNoises = new float[owner.noiseSlots.Count];
            this.columnNoiseStamps = new int[owner.noiseSlots.Count];
            this.blockNoises = new float[owner.noiseSlots.Count];
            this.blockNoiseStamps = new int[owner.noiseSlots.Count];

            if (biomeNames is not null)
            {
                this.reachableIds = owner.ReachableIds(biomeNames);
                this.onlyBiome = biomeNames.Count == 1 ? biomeNames[0] : null;
            }

            this.volume = volume ?? MaterialVolume(terrain);
        }

        // Vanilla's material volume ends at the top of the highest nonempty section, before extensions.
        private static DensityVolume MaterialVolume(TerrainContext terrain)
        {
            var highest = terrain.Volume.MinBlockY;

            for (var x = 0; x < 16; x++)
            {
                for (var z = 0; z < 16; z++)
                {
                    highest = Math.Max(highest, SurfaceHeight(terrain.Chunk, x, z) - 1);
                }
            }

            var sectionTop = (highest & ~15) + 15;

            return terrain.Volume with { SizeY = Math.Max(1, sectionTop - terrain.Volume.MinBlockY + 1) };
        }

        public TerrainContext Terrain { get; }

        /// <summary>The column within the chunk.</summary>
        public int LocalX { get; private set; }

        public int LocalZ { get; private set; }

        public int X { get; private set; }

        public int Y { get; private set; }

        public int Z { get; private set; }

        public int Above { get; private set; }

        public int Below { get; private set; }

        public int Water { get; private set; }

        public int SurfaceDepth { get; private set; }

        public int GradientX { get; private set; }

        public int GradientZ { get; private set; }

        /// <summary>Whether the terrain rises by 4 or more blocks towards the north or east within the chunk.</summary>
        public bool Steep => this.GradientX <= -4 || this.GradientZ >= 4;

        /// <summary>The block as plugins' rules and conditions see it, made on first use.</summary>
        public ISurfaceContext Surface => field ??= new PluginSurfaceContext(this);

        public string Biome => this.biome ??= this.onlyBiome ?? this.biomeAt(this.X, this.Y, this.Z);

        /// <summary>The <see cref="Biome"/>'s number among those biome conditions name, or -1.</summary>
        public int BiomeId => this.biomeId ??= this.NumberBiome(this.Biome);

        /// <summary>The surface secondary noise of the column, which stone depth conditions widen by.</summary>
        public double Secondary => this.owner.SecondarySlot < 0
            ? this.owner.SecondaryNoise.Get(this.X, 0.0, this.Z)
            : this.Noise(this.owner.SecondarySlot, this.owner.SecondaryNoise, false);

        public int MinSurface => this.minSurface ??= this.ComputeMinSurface();

        private int ComputeMinSurface()
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
                var surfaceLevel = this.owner.generator.Router.Functions["chunk_surface_level"];
                this.surfaceLevels = new float[plane.Size];
                surfaceLevel.SampleVolume(this.surfaceLevels, plane);
            }

            var index = plane.Index(this.X - plane.MinBlockX, 0, this.Z - plane.MinBlockZ);
            var level = (int)MathF.Floor(this.surfaceLevels[index]);

            return level + this.SurfaceDepth - 8;
        }

        /// <summary>
        /// Whether the block's biome is one of <paramref name="ids"/>, numbered as <see cref="BiomeId"/> is. Biomes the
        /// chunk can't reach are ruled out without looking the block's biome up.
        /// </summary>
        public bool IsBiome(int[] ids)
        {
            if (this.reachableIds is bool[] reachable && !AnyReachable(reachable, ids))
                return false;

            return ids.AsSpan().Contains(this.BiomeId);
        }

        private static bool AnyReachable(bool[] reachable, int[] ids)
        {
            // A biome numbered after the context was made may be any.
            foreach (var id in ids)
            {
                if (id >= reachable.Length || reachable[id])
                    return true;
            }

            return false;
        }

        private int NumberBiome(string name)
        {
            if (!ReferenceEquals(name, this.lastNumberedBiome))
            {
                this.lastBiomeId = this.owner.FindBiomeId(name);
                this.lastNumberedBiome = name;
            }

            return this.lastBiomeId;
        }

        public void UpdateColumn(int x, int z)
        {
            this.LocalX = x;
            this.LocalZ = z;
            this.X = this.Terrain.Volume.BlockX(x);
            this.Z = this.Terrain.Volume.BlockZ(z);
            this.columnStamp++;
            this.minSurface = null;

            var random = new PositionalRandom(this.owner.generator.Compiler.Random, this.X, 0, this.Z);
            double noise = this.owner.SurfaceNoise.Get(this.X, 0.0, this.Z);
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
            this.biomeId = null;
            this.blockStamp++;
            this.volumeIndex = this.VolumeIndex();
        }

        private int VolumeIndex()
        {
            var x = this.X - this.volume.MinBlockX;
            var y = this.Y - this.volume.MinBlockY;
            var z = this.Z - this.volume.MinBlockZ;
            var inside = (uint)x < this.volume.SizeX && (uint)y < this.volume.SizeY && (uint)z < this.volume.SizeZ;

            return inside ? this.volume.Index(x, y, z) : -1;
        }

        /// <summary>A noise at the column, or at the block when <paramref name="is3d"/>, sampled once each.</summary>
        /// <param name="slot">The noise's <see cref="NoiseSlot"/>.</param>
        public float Noise(int slot, NoiseStack noise, bool is3d)
        {
            if (slot >= this.columnNoises.Length)
            {
                var length = this.owner.noiseSlots.Count;
                Array.Resize(ref this.columnNoises, length);
                Array.Resize(ref this.columnNoiseStamps, length);
                Array.Resize(ref this.blockNoises, length);
                Array.Resize(ref this.blockNoiseStamps, length);
            }

            var values = is3d ? this.blockNoises : this.columnNoises;
            var stamps = is3d ? this.blockNoiseStamps : this.columnNoiseStamps;
            var stamp = is3d ? this.blockStamp : this.columnStamp;

            if (stamps[slot] != stamp)
            {
                values[slot] = noise.Get(this.X, is3d ? this.Y : 0.0, this.Z);
                stamps[slot] = stamp;
            }

            return values[slot];
        }

        /// <summary>A density at the block, from the material volume's samples while the block is inside it.</summary>
        /// <param name="slot">The function's <see cref="DensitySlot"/>.</param>
        public float Density(int slot, DensityFunction function)
        {
            if (this.volumeIndex < 0)
            {
                return function.Sample(this.X, this.Y, this.Z);
            }

            if (slot >= this.densities.Length)
            {
                Array.Resize(ref this.densities, this.owner.densitySlots.Count);
            }

            if (this.densities[slot] is not float[] values)
            {
                values = ArrayPool<float>.Shared.Rent(this.volume.Size);
                function.SampleVolume(values.AsSpan(0, this.volume.Size), this.volume);
                this.densities[slot] = values;
            }

            return values[this.volumeIndex];
        }

        /// <summary>A density sampled at the block's point, as vanilla's ore gap is.</summary>
        public float PointDensity(DensityFunction function) => function.Sample(this.X, this.Y, this.Z);

        public void Dispose()
        {
            for (var slot = 0; slot < this.densities.Length; slot++)
            {
                if (this.densities[slot] is float[] values)
                    ArrayPool<float>.Shared.Return(values);

                this.densities[slot] = null;
            }
        }

        public IBlock GetBlock(int y)
        {
            var chunk = this.Terrain.Chunk;

            if (y < chunk.MinY || y >= chunk.MinY + chunk.Height)
            {
                return BlocksRegistry.Air;
            }

            return chunk.GetBlock(this.LocalX, y, this.LocalZ);
        }

        public void SetBlock(int y, IBlock block)
        {
            var chunk = this.Terrain.Chunk;

            if (y < chunk.MinY || y >= chunk.MinY + chunk.Height)
            {
                return;
            }

            // The chunk keeps its heightmaps, including the worldgen ones later columns read, up to date.
            chunk.SetBlock(this.LocalX, y, this.LocalZ, block);

            // Vanilla's material column marks every state with a fluid for post-processing, such as the water of
            // swamp rules. Unlike density fill, it doesn't consult the aquifer's scheduling decision.
            if (block.HasFluid())
            {
                this.Terrain.MarkForPostProcessing(this.X, y, this.Z);
            }
        }
    }
}
