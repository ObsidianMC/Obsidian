using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.WorldData.Structures.Pools;

/// <summary>
/// Assembles jigsaw structures piece by piece, like vanilla's <c>JigsawPlacement</c>.
/// </summary>
internal static class JigsawPlacement
{
    private static readonly StructureRotation[] rotations = Enum.GetValues<StructureRotation>();

    /// <summary>
    /// Vanilla <c>addPieces</c>: picks the start piece and, if it fits, returns the stub that grows the structure from it.
    /// </summary>
    /// <param name="startJigsawName">The start piece is placed so its jigsaw with this name is at <paramref name="position"/>.</param>
    /// <param name="maxDepth">How many pieces away from the start the structure may grow.</param>
    /// <param name="projectStartToHeightmap">Moves the start onto this heightmap, offset by <paramref name="position"/>'s Y.</param>
    /// <param name="maxDistance">How far from the start's center pieces may reach, horizontally and vertically.</param>
    /// <param name="dimensionPadding">Blocks kept free at the bottom and top of the world, or <c>null</c> for none.</param>
    public static StructureStub? AddPieces(StructureGenerationContext context, StructureTemplatePool startPool, string? startJigsawName,
        int maxDepth, Vector position, bool useExpansionHack, HeightmapType? projectStartToHeightmap, int maxDistance,
        PoolAliasLookup aliases, int? dimensionPadding, LiquidSettings liquidSettings)
    {
        var random = context.Random;
        var rotation = StructureRotationExtensions.Random(random);
        var pool = StructureTemplatePool.Get(aliases.Lookup(startPool.Identifier)) ?? startPool;
        var element = pool.GetRandomTemplate(random);
        if (element is EmptyPoolElement)
            return null;

        var anchor = position;
        if (startJigsawName is not null)
        {
            var named = element.GetShuffledJigsawBlocks(position, rotation, random).FirstOrDefault(jigsaw => jigsaw.Name == startJigsawName);
            if (named is null)
                return null;

            anchor = named.Info.Position;
        }

        var offset = anchor - position;
        var startPosition = position - offset;
        var start = new PoolElementStructurePiece(element, startPosition, element.GroundLevelDelta, rotation,
            element.GetBoundingBox(startPosition, rotation), liquidSettings);

        var box = start.BoundingBox;
        var centerX = (box.MaxX + box.MinX) / 2;
        var centerZ = (box.MaxZ + box.MinZ) / 2;
        var y = projectStartToHeightmap is null
            ? startPosition.Y
            : position.Y + context.Terrain.GetBaseHeight(centerX, centerZ, projectStartToHeightmap.Value);
        start.Move(0, y - (box.MinY + start.GroundLevelDelta), 0);

        if (IsStartTooCloseToWorldHeightLimits(context, dimensionPadding, start.BoundingBox))
            return null;

        var centerY = y + offset.Y;
        return new StructureStub(new Vector(centerX, centerY, centerZ), builder =>
        {
            List<PoolElementStructurePiece> pieces = [start];
            if (maxDepth > 0)
            {
                var bottom = dimensionPadding ?? 0;
                var limits = BlockBox.Create(centerX - maxDistance, Math.Max(centerY - maxDistance, context.MinY + bottom), centerZ - maxDistance,
                    centerX + maxDistance, Math.Min(centerY + maxDistance + 1, context.MinY + context.Height - bottom) - 1, centerZ + maxDistance);
                var free = new FreeSpace(limits);
                free.Remove(start.BoundingBox);

                var placer = new Placer(context, maxDepth, pieces, aliases, useExpansionHack, liquidSettings);
                placer.Run(start, free);
            }

            foreach (var piece in pieces)
                builder.AddPiece(piece);
        });
    }

    /// <summary>
    /// Vanilla <c>isStartTooCloseToWorldHeightLimits</c>: with padding, the start piece must stay inside the padded range.
    /// </summary>
    private static bool IsStartTooCloseToWorldHeightLimits(StructureGenerationContext context, int? padding, BlockBox box) =>
        padding is not null && (box.MinY < context.MinY + padding.Value || box.MaxY > context.MinY + context.Height - 1 - padding.Value);

    /// <summary>
    /// The space pieces may still take, like vanilla's free <c>VoxelShape</c>: a box minus the boxes of placed pieces.
    /// Shared between the pieces growing into it.
    /// </summary>
    private sealed class FreeSpace(BlockBox bounds)
    {
        private readonly List<BlockBox> taken = [];

        /// <summary>Whether every block of <paramref name="box"/> is free.</summary>
        public bool Fits(BlockBox box)
        {
            if (box.MinX < bounds.MinX || box.MinY < bounds.MinY || box.MinZ < bounds.MinZ
                || box.MaxX > bounds.MaxX || box.MaxY > bounds.MaxY || box.MaxZ > bounds.MaxZ)
                return false;

            foreach (var other in this.taken)
            {
                if (other.Intersects(box))
                    return false;
            }

            return true;
        }

        public void Remove(BlockBox box) => this.taken.Add(box);
    }

    private sealed record PieceState(PoolElementStructurePiece Piece, FreeSpace Free, int Depth);

    /// <summary>
    /// Vanilla <c>JigsawPlacement.Placer</c>: expands pieces in placement priority order, attaching a fitting piece to each
    /// of their jigsaws.
    /// </summary>
    private sealed class Placer(StructureGenerationContext context, int maxDepth, List<PoolElementStructurePiece> pieces,
        PoolAliasLookup aliases, bool useExpansionHack, LiquidSettings liquidSettings)
    {
        private readonly IRandomSource random = context.Random;
        private readonly PriorityQueue placing = new();

        public void Run(PoolElementStructurePiece start, FreeSpace free)
        {
            this.TryPlacingChildren(start, free, 0);
            while (this.placing.TryDequeue(out var state))
                this.TryPlacingChildren(state.Piece, state.Free, state.Depth);
        }

        private void TryPlacingChildren(PoolElementStructurePiece piece, FreeSpace free, int depth)
        {
            var element = piece.Element;
            var rigid = element.Projection == Projection.Rigid;
            FreeSpace? inside = null;
            var box = piece.BoundingBox;
            var minY = box.MinY;

            foreach (var jigsaw in element.GetShuffledJigsawBlocks(piece.Position, piece.ElementRotation, this.random))
            {
                var facing = jigsaw.FrontFacing;
                var jigsawPosition = jigsaw.Info.Position;
                var target = jigsawPosition + facing.ToVector();
                var relativeY = jigsawPosition.Y - minY;
                var surfaceY = int.MinValue;

                var pool = StructureTemplatePool.Get(aliases.Lookup(jigsaw.Pool));
                if (pool is null || pool.Size == 0 && pool.Identifier != StructureTemplatePool.EmptyId)
                    continue;

                var fallback = pool.FallbackPool;
                if (fallback is null || fallback.Size == 0 && fallback.Identifier != StructureTemplatePool.EmptyId)
                    continue;

                var space = box.IsInside(target) ? inside ??= new FreeSpace(box) : free;

                var candidates = new List<StructurePoolElement>();
                if (depth != maxDepth)
                    candidates.AddRange(pool.GetShuffledTemplates(this.random));

                candidates.AddRange(fallback.GetShuffledTemplates(this.random));

                foreach (var candidate in candidates)
                {
                    if (candidate is EmptyPoolElement)
                        break;

                    if (this.TryAttach(piece, jigsaw, target, relativeY, rigid, candidate, space, depth, ref surfaceY))
                        break;
                }
            }
        }

        /// <summary>
        /// Tries the candidate in every rotation (shuffled) against each of its jigsaws that can attach.
        /// </summary>
        /// <param name="surfaceY">The surface height at the parent's jigsaw, computed on first use.</param>
        /// <returns>Whether the candidate was placed.</returns>
        private bool TryAttach(PoolElementStructurePiece piece, JigsawBlockInfo jigsaw, Vector target, int relativeY, bool rigid,
            StructurePoolElement candidate, FreeSpace space, int depth, ref int surfaceY)
        {
            var minY = piece.BoundingBox.MinY;
            var jigsawPosition = jigsaw.Info.Position;

            foreach (var rotation in FeatureHelpers.ShuffledCopy(rotations, this.random))
            {
                var candidateJigsaws = candidate.GetShuffledJigsawBlocks(Vector.Zero, rotation, this.random);
                var candidateBox = candidate.GetBoundingBox(Vector.Zero, rotation);
                var expansion = useExpansionHack && candidateBox.YSpan <= 16 ? this.ExpansionHeight(candidateJigsaws, candidateBox) : 0;

                foreach (var candidateJigsaw in candidateJigsaws)
                {
                    if (!jigsaw.CanAttach(candidateJigsaw))
                        continue;

                    var candidateJigsawPosition = candidateJigsaw.Info.Position;
                    var offset = target - candidateJigsawPosition;
                    var placedBox = candidate.GetBoundingBox(offset, rotation);
                    var candidateRigid = candidate.Projection == Projection.Rigid;
                    var candidateJigsawY = candidateJigsawPosition.Y;
                    var deltaY = relativeY - candidateJigsawY + jigsaw.FrontFacing.ToVector().Y;

                    int targetY;
                    if (rigid && candidateRigid)
                    {
                        targetY = minY + deltaY;
                    }
                    else
                    {
                        if (surfaceY == int.MinValue)
                            surfaceY = context.Terrain.GetBaseHeight(jigsawPosition.X, jigsawPosition.Z, HeightmapType.WorldSurfaceWG);

                        targetY = surfaceY - candidateJigsawY;
                    }

                    var yOffset = targetY - placedBox.MinY;
                    placedBox = placedBox.Move(0, yOffset, 0);
                    var placedPosition = offset + (0, yOffset, 0);

                    if (expansion > 0)
                    {
                        var height = Math.Max(expansion + 1, placedBox.MaxY - placedBox.MinY);
                        placedBox = placedBox.Encapsulate(new Vector(placedBox.MinX, placedBox.MinY + height, placedBox.MinZ));
                    }

                    if (!space.Fits(placedBox))
                        continue;

                    space.Remove(placedBox);
                    var groundLevelDelta = piece.GroundLevelDelta;
                    var candidateGroundLevelDelta = candidateRigid ? groundLevelDelta - deltaY : candidate.GroundLevelDelta;
                    var child = new PoolElementStructurePiece(candidate, placedPosition, candidateGroundLevelDelta, rotation, placedBox, liquidSettings);

                    int junctionY;
                    if (rigid)
                    {
                        junctionY = minY + relativeY;
                    }
                    else if (candidateRigid)
                    {
                        junctionY = targetY + candidateJigsawY;
                    }
                    else
                    {
                        if (surfaceY == int.MinValue)
                            surfaceY = context.Terrain.GetBaseHeight(jigsawPosition.X, jigsawPosition.Z, HeightmapType.WorldSurfaceWG);

                        junctionY = surfaceY + deltaY / 2;
                    }

                    piece.AddJunction(new JigsawJunction(target.X, junctionY - relativeY + groundLevelDelta, target.Z, deltaY, candidateRigid));
                    child.AddJunction(new JigsawJunction(jigsawPosition.X, junctionY - candidateJigsawY + candidateGroundLevelDelta, jigsawPosition.Z,
                        -deltaY, rigid));
                    pieces.Add(child);

                    if (depth + 1 <= maxDepth)
                        this.placing.Enqueue(new PieceState(child, space, depth + 1), jigsaw.PlacementPriority);

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Vanilla's expansion hack: the height of the tallest pool reachable through the candidate's inner jigsaws, so
        /// short pieces reserve room for what grows on top of them.
        /// </summary>
        private int ExpansionHeight(List<JigsawBlockInfo> candidateJigsaws, BlockBox candidateBox)
        {
            var height = 0;
            foreach (var candidateJigsaw in candidateJigsaws)
            {
                if (!candidateBox.IsInside(candidateJigsaw.Info.Position + candidateJigsaw.FrontFacing.ToVector()))
                    continue;

                var pool = StructureTemplatePool.Get(aliases.Lookup(candidateJigsaw.Pool));
                var fallback = pool?.FallbackPool;
                height = Math.Max(height, Math.Max(pool?.MaxSize ?? 0, fallback?.MaxSize ?? 0));
            }

            return height;
        }
    }

    /// <summary>
    /// Vanilla <c>SequencedPriorityIterator</c>: the highest priority first, first in first out within a priority.
    /// </summary>
    private sealed class PriorityQueue
    {
        private readonly SortedDictionary<int, Queue<PieceState>> queues = new(Comparer<int>.Create((a, b) => b.CompareTo(a)));

        public void Enqueue(PieceState state, int priority)
        {
            if (!this.queues.TryGetValue(priority, out var queue))
                this.queues[priority] = queue = new Queue<PieceState>();

            queue.Enqueue(state);
        }

        public bool TryDequeue([MaybeNullWhen(false)] out PieceState state)
        {
            foreach (var (priority, queue) in this.queues)
            {
                if (queue.TryDequeue(out state))
                {
                    if (queue.Count == 0)
                        this.queues.Remove(priority);

                    return true;
                }
            }

            state = null;
            return false;
        }
    }
}
