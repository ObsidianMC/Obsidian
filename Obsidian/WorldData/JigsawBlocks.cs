using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Generators.Features;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Terrain;
using Obsidian.WorldData.Portals;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Structures.Pools;

namespace Obsidian.WorldData;

/// <summary>Runs the jigsaw editor against the same pools and assembly code as world generation.</summary>
internal static class JigsawBlocks
{
    internal static bool TryIdentifier(string text, out string identifier)
    {
        var colon = text.IndexOf(':');
        var ns = colon <= 0 ? "minecraft" : text[..colon];
        var path = colon < 0 ? text : text[(colon + 1)..];
        identifier = ns + ":" + path;
        return ns.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.')
            && path.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.' or '/');
    }

    internal static async Task GenerateAsync(AbstractLevel level, Vector editorPosition, int levels, bool keepJigsaws)
    {
        JigsawBlockEntity editor;
        JigsawBlockSettings settings;
        IBlock editorState;
        Vector origin;
        lock (level.Fluids.TickLock)
        {
            if (level.GetResidentBlockEntity(editorPosition) is not JigsawBlockEntity current)
                return;

            editor = current;
            settings = editor.Settings;
            editorState = level.BlockUpdates.GetBlock(editorPosition);
            origin = editorPosition + JigsawBlockInfo.GetFrontFacing(editorState).ToVector();
        }

        var generator = level.Generator as MojangGenerator;
        var features = generator?.Features?.Invoke() ?? PluginFeatures.CurrentRegistry;
        var structures = generator?.Structures?.Invoke() ?? PluginStructures.CurrentRegistry;
        var pool = structures.Pool(settings.Pool)
            ?? throw new InvalidOperationException($"Unknown jigsaw template pool '{settings.Pool}'");
        var templates = features.CaptureTemplates(structures.Templates);
        var store = StructureBlockTemplates.For(level);
        StructureTemplate Template(string name) => store.ReadAsync(name).GetAwaiter().GetResult() ?? templates(name);
        var terrain = generator?.Builder?.StructureTerrain ?? new FlatTerrain(level);
        var noise = generator?.Dimension.Settings;
        var generation = new WorldGenerationContext(
            Math.Max(level.MinY, noise?.MinY ?? level.MinY),
            Math.Min(level.Height, noise?.Height ?? level.Height));
        var random = new WorldgenRandom(new LegacyRandomSource(0));
        random.SetLargeFeatureSeed(level.LevelData.RandomSeed, origin.X >> 4, origin.Z >> 4);
        var context = new StructureGenerationContext
        {
            ChunkX = origin.X >> 4,
            ChunkZ = origin.Z >> 4,
            Seed = level.LevelData.RandomSeed,
            Random = random,
            Terrain = terrain,
            // The editor accepts every biome; assembly only uses the noise terrain queries.
            BiomeSource = new FixedBiomeSource(CodecRegistry.Biomes.Plains),
            ValidBiome = static _ => true,
            MinY = level.MinY,
            Height = level.Height,
            Generation = generation,
            Pools = structures.Pool
        };

        IReadOnlyList<StructurePiece> pieces;
        using (StructureRegistry.Use(Template))
        using (FeatureRegistry.UseTemplates(Template))
            pieces = JigsawPlacement.Generate(context, pool, settings.Target, levels, origin);

        // Assembly itself uses noise, not loaded terrain. Only load chunks reached by actual pieces, never the
        // whole 128-block search radius. Keep their tickets until placement has completed.
        var leases = new List<PortalArea>();
        var chunks = new HashSet<(int X, int Z)>();
        try
        {
            foreach (var piece in pieces)
            {
                var box = piece.BoundingBox;
                if (box.MaxY < level.MinY || box.MinY >= level.MinY + level.Height)
                    continue;

                for (var x = box.MinX >> 4; x <= box.MaxX >> 4; x++)
                {
                    for (var z = box.MinZ >> 4; z <= box.MaxZ >> 4; z++)
                    {
                        if (!chunks.Add((x, z)))
                            continue;

                        var lease = await PortalArea.LoadAsync(level, new Vector(x << 4, 0, z << 4), 0,
                            () => !level.IsDisposed);
                        if (lease is null)
                            return;

                        leases.Add(lease);
                    }
                }
            }

            lock (level.Fluids.TickLock)
            {
                if (editor.IsRemoved || !ReferenceEquals(editor.Settings, settings)
                    || !ReferenceEquals(level.GetResidentBlockEntity(editorPosition), editor)
                    || !level.BlockUpdates.GetBlock(editorPosition).IsSameState(editorState))
                {
                    return;
                }

                using var scope = StructureRegistry.Use(Template);
                using var featureScope = FeatureRegistry.UseTemplates(Template);
                var view = new StructureBlockLevel(level, strict: false, knownShape: true);
                var bounds = new BlockBox(new Vector(int.MinValue), new Vector(int.MaxValue));
                var placement = new StructurePieceContext(view, view.Random, bounds,
                    origin.X >> 4, origin.Z >> 4, origin)
                {
                    Terrain = terrain,
                    Generation = context.Generation
                };
                foreach (var piece in pieces.OfType<PoolElementStructurePiece>())
                {
                    piece.Element.Place(placement, piece.Position, origin, piece.ElementRotation,
                        placement.Box, placement.Random, piece.LiquidSettings, keepJigsaws);
                }
            }
        }
        finally
        {
            foreach (var lease in leases)
                lease.Dispose();
        }
    }

    private sealed class FlatTerrain(AbstractLevel level) : IStructureTerrain
    {
        private readonly IBlock[] layers = level.Generator is SuperflatGenerator flat
            ? flat.Settings.Layers.ToArray() : [];

        public int SeaLevel => -63;

        public int GetBaseHeight(int x, int z, HeightmapType heightmap)
        {
            for (var y = Math.Min(this.layers.Length, level.Height) - 1; y >= 0; y--)
            {
                if (WorldgenHeightmaps.Matches(heightmap, this.layers[y]))
                    return level.MinY + y + 1;
            }

            return level.MinY;
        }

        public NoiseColumn GetBaseColumn(int x, int z) => new(level.MinY, this.layers);
    }
}
