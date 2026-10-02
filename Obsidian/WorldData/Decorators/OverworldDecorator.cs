using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.ChunkData;
using Obsidian.WorldData.Features.Flora;
using Obsidian.WorldData.Generators;
using System.Diagnostics;
using System.Linq.Expressions;

namespace Obsidian.WorldData.Decorators;

public static class OverworldDecorator
{
    private static readonly ConcurrentDictionary<Type, Func<GenHelper, IChunk, BaseFlora>> floraCache = new();

    private static readonly Type[] argumentCache = [typeof(GenHelper), typeof(IChunk)];
    public static readonly ParameterExpression[] expressionParameters = argumentCache.Select((t, i) => Expression.Parameter(t, $"param{i}")).ToArray();

    static OverworldDecorator()
    {
        var asm = typeof(OverworldDecorator).Assembly;

        var floras = asm.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(BaseFlora)));

        foreach (var floraType in floras)
        {
            var ctor = floraType.GetConstructor(argumentCache);

            var expression = Expression.New(ctor, expressionParameters);
            var lambda = Expression.Lambda<Func<GenHelper, IChunk, BaseFlora>>(expression, expressionParameters);

            var compiledLamda = lambda.Compile();

            floraCache.TryAdd(floraType, compiledLamda);
        }
    }

    public static async Task DecorateAsync(IChunk chunk, GenHelper helper)
    {
        var treeLevel = await LegacyTreeLevel.CreateAsync(chunk, helper);

        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                int y = chunk.Heightmaps[HeightmapType.WorldSurfaceWG].GetHeight(x, z);
                var chunkPos = new Vector(x, y, z);
                var biome = CodecRegistry.GetBiome((int)helper.Noise.Biome.GetValue((chunk.X << 4) + x, y, (chunk.Z << 4) + z));
                var decorator = DecoratorFactory.GetDecorator(biome, chunk, chunkPos, helper);

                decorator.Decorate();
                GenerateTrees(chunkPos + (chunk.X << 4, 0, chunk.Z << 4), decorator.Features, helper, treeLevel);
                await GenerateFloraAsync(chunkPos + (chunk.X << 4, 0, chunk.Z << 4), decorator.Features, helper, chunk);
            }
        }
    }

    internal static async Task GenerateFloraAsync(Vector pos, DecoratorFeatures features, GenHelper helper, IChunk chunk)
    {
        for (int i = 0; i < features.Flora.Count; i++)
        {
            var flora = features.Flora[i];

            if (flora.Frequency == 0)
                continue;

            if (!floraCache.TryGetValue(flora.FloraType, out var floraFactory))
                throw new UnreachableException();

            var floraInstance = floraFactory(helper, chunk);

            var noiseVal = helper.Noise.Decoration.GetValue(pos.X, -33 + (i * 22), pos.Z);
            var freq = flora.Frequency / 200.0;
            bool isFlora = noiseVal > 0.9 && noiseVal <= freq + 0.9;
            if (!isFlora) { continue; }

            await floraInstance.GenerateFloraAsync(pos, helper.Seed, flora.Radius, flora.Density);
        }

    }

    /// <summary>
    /// Like <see cref="GenerateTrees"/>, over the already loaded chunks around <paramref name="pos"/>.
    /// </summary>
    internal static async Task GenerateTreesAsync(Vector pos, DecoratorFeatures features, GenHelper helper)
    {
        if (features.Trees.Count == 0)
            return;

        var level = await LegacyTreeLevel.CreateAsync(pos.X >> 4, pos.Z >> 4, helper, center: null);
        GenerateTrees(pos, features, helper, level);
    }

    /// <summary>
    /// Places the biome's trees on the surface block at <paramref name="pos"/> (world coordinates) using the
    /// synchronous feature API over <paramref name="level"/>.
    /// </summary>
    internal static void GenerateTrees(Vector pos, DecoratorFeatures features, GenHelper helper, IWorldGenLevel level)
    {
        for (int i = 0; i < features.Trees.Count; i++)
        {
            var tree = features.Trees[i];

            if (tree.Frequency == 0 || tree.Feature is null)
                continue;

            // Use a different noisemap for each tree type by setting another Y value.
            var noiseVal = helper.Noise.Decoration.GetValue(pos.X, -45 + (i * 10), pos.Z);
            var freq = tree.Frequency / 100.0;
            bool isTree = noiseVal > 0.8 && noiseVal <= freq + 0.8;
            if (!isTree) { continue; }

            // Vanilla tree features start at the first block above the ground.
            tree.Feature.Place(new FeatureContext
            {
                Level = level,
                Origin = pos + Vector.Up,
                Random = new XoroshiroRandomSource(helper.Seed ^ pos.GetHashCode()),
                Generation = new WorldGenerationContext(level.MinY, level.Height)
            });
        }
    }

    /// <summary>
    /// <see cref="IWorldGenLevel"/> over the chunk being decorated and whichever of its 8 neighbors are already loaded,
    /// for the legacy generator's tree placement. Positions in missing chunks read as air and can't be written.
    /// </summary>
    private sealed class LegacyTreeLevel : IWorldGenLevel
    {
        private readonly Dictionary<(int X, int Z), IChunk> chunks;

        private LegacyTreeLevel(long seed, Dictionary<(int X, int Z), IChunk> chunks)
        {
            this.Seed = seed;
            this.chunks = chunks;
        }

        public long Seed { get; }

        public int MinY => -64;

        public int Height => 384;

        public int SeaLevel => 63;

        /// <summary>Preloads the 3x3 chunks around <paramref name="center"/> without scheduling generation.</summary>
        public static Task<LegacyTreeLevel> CreateAsync(IChunk center, GenHelper helper) => CreateAsync(center.X, center.Z, helper, center);

        /// <summary>
        /// Preloads the 3x3 chunks around (<paramref name="chunkX"/>, <paramref name="chunkZ"/>) without scheduling
        /// generation; <paramref name="center"/>, if given, is the chunk being generated (not yet in the world).
        /// </summary>
        public static async Task<LegacyTreeLevel> CreateAsync(int chunkX, int chunkZ, GenHelper helper, IChunk? center)
        {
            var chunks = new Dictionary<(int X, int Z), IChunk>();
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                {
                    var chunk = dx == 0 && dz == 0 && center is not null
                        ? center
                        : await helper.World.GetChunkAsync(chunkX + dx, chunkZ + dz, scheduleGeneration: false);

                    if (chunk is not null)
                        chunks[(chunkX + dx, chunkZ + dz)] = chunk;
                }
            }

            return new LegacyTreeLevel(helper.Seed, chunks);
        }

        public IBlock GetBlock(Vector position)
        {
            var chunk = this.ChunkAt(position);
            return chunk is null || this.IsOutsideBuildHeight(position.Y) ? BlocksRegistry.Air : chunk.GetBlock(position);
        }

        public bool SetBlock(Vector position, IBlock block)
        {
            if (!this.EnsureCanWrite(position))
                return false;

            var chunk = this.ChunkAt(position)!;
            chunk.SetBlock(position, block);
            DataBlockEntity.ApplyBlockChange(chunk, position, block);
            return true;
        }

        public bool EnsureCanWrite(Vector position) => this.ChunkAt(position) is not null && !this.IsOutsideBuildHeight(position.Y);

        public int GetHeight(HeightmapType type, int x, int z)
        {
            var chunk = this.ChunkAt(new Vector(x, 0, z));
            if (chunk is null)
                return this.MinY;

            for (var y = this.MinY + this.Height - 1; y >= this.MinY; y--)
            {
                if (MatchesHeightmap(type, chunk.GetBlock(x, y, z)))
                    return y + 1;
            }

            return this.MinY;
        }

        public BiomeCodec GetBiome(Vector position) =>
            (this.ChunkAt(position) ?? this.chunks.Values.First()).GetBiome(position);

        public void ScheduleFluidTick(Vector position)
        {
            // Trees never schedule fluid ticks.
        }

        public void SetBlockEntity(Vector position, IBlockEntity blockEntity) =>
            this.ChunkAt(position)?.SetBlockEntity(position.X, position.Y, position.Z, blockEntity);

        public void AddEntity(GeneratedEntity entity)
        {
            // Trees never add entities.
        }

        public void MarkForPostProcessing(Vector position)
        {
            // Trees never mark blocks for post-processing.
        }

        public IBlockEntity? GetBlockEntity(Vector position) =>
            this.IsOutsideBuildHeight(position.Y) ? null : this.ChunkAt(position)?.GetBlockEntity(position.X, position.Y, position.Z);

        private IChunk? ChunkAt(Vector position) => this.chunks.GetValueOrDefault((position.X >> 4, position.Z >> 4));

        private bool IsOutsideBuildHeight(int y) => y < this.MinY || y >= this.MinY + this.Height;

        // Vanilla heightmap predicates, approximated with the block physics data.
        private static bool MatchesHeightmap(HeightmapType type, IBlock block) => type switch
        {
            HeightmapType.WorldSurface or HeightmapType.WorldSurfaceWG => !block.IsAir,
            HeightmapType.OceanFloor or HeightmapType.OceanFloorWG => block.BlocksMotion(),
            HeightmapType.MotionBlocking => block.BlocksMotion() || block.HasFluid(),
            _ => (block.BlocksMotion() || block.HasFluid()) && block.BlockClass() is not ("TintedParticleLeavesBlock"
                or "UntintedParticleLeavesBlock" or "MangroveLeavesBlock" or "LeavesBlock")
        };
    }
}
