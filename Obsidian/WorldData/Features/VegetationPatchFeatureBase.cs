using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// Shared logic of vanilla's VegetationPatchFeature: replaces the cave floor or ceiling in a rough ellipse with
/// <see cref="GroundState"/> and scatters <see cref="VegetationFeature"/> on it.
/// </summary>
/// <remarks>
/// Vanilla keeps the ground positions in a <c>HashSet&lt;BlockPos&gt;</c> and rolls the vegetation chance while iterating it,
/// so the set's Java iteration order is reproduced with <see cref="FeatureHelpers.JavaHashSetOrder"/>.
/// </remarks>
public abstract class VegetationPatchFeatureBase : ConfiguredFeatureBase
{
    /// <summary>
    /// Blocks the ground may replace.
    /// </summary>
    public required BlockSet Replaceable { get; init; }

    public required IBlockStateProvider GroundState { get; init; }

    public required PlacedFeature VegetationFeature { get; init; }

    public required CaveSurface Surface { get; init; }

    /// <summary>
    /// Ground thickness.
    /// </summary>
    public required IIntProvider Depth { get; init; }

    public required float ExtraBottomBlockChance { get; init; }

    /// <summary>
    /// How far the column search moves up or down to find the surface.
    /// </summary>
    public required int VerticalRange { get; init; }

    public required float VegetationChance { get; init; }

    public required IIntProvider XzRadius { get; init; }

    public required float ExtraEdgeColumnChance { get; init; }

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        if (!level.EnsureCanWrite(context.Origin))
            return false;

        var radiusX = this.XzRadius.Sample(random) + 1;
        var radiusZ = this.XzRadius.Sample(random) + 1;
        var ground = this.PlaceGroundPatch(level, random, context.Origin, radiusX, radiusZ);

        foreach (var position in ground)
        {
            if (this.VegetationChance > 0.0f && random.NextFloat() < this.VegetationChance)
                this.PlaceVegetation(context, position);
        }

        return ground.Count > 0;
    }

    /// <summary>
    /// Places the ground and returns the ground positions in vanilla's iteration order.
    /// </summary>
    protected virtual List<Vector> PlaceGroundPatch(IWorldGenLevel level, IRandomSource random, Vector origin, int radiusX, int radiusZ)
    {
        var toward = this.Surface.Direction();
        var away = toward.Opposite();
        var placed = RentPositions();
        try
        {
            for (var dx = -radiusX; dx <= radiusX; dx++)
            {
                var edgeX = dx == -radiusX || dx == radiusX;

                for (var dz = -radiusZ; dz <= radiusZ; dz++)
                {
                    var edgeZ = dz == -radiusZ || dz == radiusZ;
                    var corner = edgeX && edgeZ;
                    var edge = (edgeX || edgeZ) && !corner;

                    // Corners are skipped; other edge columns survive with ExtraEdgeColumnChance (rolled only for them).
                    if (corner || edge && (this.ExtraEdgeColumnChance == 0.0f || random.NextFloat() > this.ExtraEdgeColumnChance))
                        continue;

                    var position = origin + new Vector(dx, 0, dz);
                    for (var i = 0; level.GetBlock(position).IsAir && i < this.VerticalRange; i++)
                        position = position.Offset(toward);

                    for (var i = 0; !level.GetBlock(position).IsAir && i < this.VerticalRange; i++)
                        position = position.Offset(away);

                    var surface = position.Offset(toward);
                    if (!level.GetBlock(position).IsAir || !level.GetBlock(surface).IsFaceSturdy(away))
                        continue;

                    var depth = this.Depth.Sample(random)
                        + (this.ExtraBottomBlockChance > 0.0f && random.NextFloat() < this.ExtraBottomBlockChance ? 1 : 0);

                    if (this.PlaceGround(level, random, surface, depth))
                        placed.Add(surface);
                }
            }

            return FeatureHelpers.JavaHashSetOrder(placed);
        }
        finally
        {
            ReturnPositions(placed);
        }
    }

    /// <summary>
    /// A list for positions collected on the way to <see cref="FeatureHelpers.JavaHashSetOrder"/>, borrowed from the
    /// thread; give it back with <see cref="ReturnPositions"/>.
    /// </summary>
    protected static List<Vector> RentPositions()
    {
        var positions = scratchPositions ?? [];
        scratchPositions = null;
        return positions;
    }

    protected static void ReturnPositions(List<Vector> positions)
    {
        positions.Clear();
        scratchPositions = positions;
    }

    [ThreadStatic]
    private static List<Vector>? scratchPositions;

    /// <summary>
    /// Places the vegetation feature on the open side of a ground position.
    /// </summary>
    protected virtual bool PlaceVegetation(FeatureContext context, Vector ground) =>
        this.VegetationFeature.Place(context.Level, context.Generation, context.Random, ground.Offset(this.Surface.Direction().Opposite()));

    private bool PlaceGround(IWorldGenLevel level, IRandomSource random, Vector start, int depth)
    {
        var toward = this.Surface.Direction();
        var position = start;

        for (var i = 0; i < depth; i++)
        {
            var state = this.GroundState.GetState(random, position);
            var existing = level.GetBlock(position);

            // Vanilla only advances when it replaces something; an already matching block repeats the same position.
            if (state.RegistryId == existing.RegistryId)
                continue;

            if (!this.Replaceable.Contains(existing))
                return i != 0;

            level.SetBlock(position, state);
            position = position.Offset(toward);
        }

        return true;
    }
}
