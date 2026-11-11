using Obsidian.API.Blocks;
using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities.AI;
using Obsidian.Registries;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>FireBlock</c> overrides: <c>tick</c> (fire burns out, or spreads to flammable neighbours),
/// <c>onPlace</c> (the fire's first tick), <c>updateShape</c> (fire that can't survive goes out), and the state fire is
/// placed in (<see cref="StateFor"/>, vanilla's <c>BaseFireBlock.getState</c>).
/// </summary>
/// <remarks>
/// Flammability is vanilla's <c>FireBlock.bootStrap</c> data, dumped as <c>fire_odds.json</c>. The tick and the
/// placement draw from the level's random in vanilla's order, which the <c>fire_ticks.json</c> capture checks. Where
/// vanilla's placement lights a portal at once, Obsidian's keeps the fire and queues the portal, which the level's
/// portal pass lights later in the same tick (see <see cref="Portals.LevelPortals.QueueFireIgnition"/>).
/// </remarks>
internal sealed partial class FireBlockBehavior : IScheduledTickBehavior, IShapeUpdateBehavior, IOnPlaceBehavior
{
    public IShapeUpdateBehavior? SharedPassRule => null;

    // Vanilla's FireBlock.MAX_AGE.
    private const int MaxAge = 15;

    // Vanilla's flags for fire ageing (260): clients aren't told, and neighbours still get shape updates.
    private const BlockUpdateFlags AgeFlags = BlockUpdateFlags.Invisible | BlockUpdateFlags.SkipBlockEntitySideEffects;

    // Vanilla's FireBlock.checkBurnOut chances: a side neighbour burns at 300, those above and below at 250.
    private const int SideOdds = 300;
    private const int VerticalOdds = 250;

    // Vanilla's EnvironmentAttributes.INCREASED_FIRE_BURNOUT: a bonus to burn-out in the attribute's places.
    private const string IncreasedFireBurnout = LevelEnvironment.IncreasedFireBurnout;

    private static readonly BlockFace[] Faces = [BlockFace.Down, BlockFace.Up, BlockFace.North, BlockFace.South, BlockFace.West, BlockFace.East];

    // The neighbours a tick tries to burn, in the order vanilla draws for them, with their chances.
    private static readonly (BlockFace Face, int Chance)[] BurnOrder =
    [
        (BlockFace.East, SideOdds),
        (BlockFace.West, SideOdds),
        (BlockFace.Down, VerticalOdds),
        (BlockFace.Up, VerticalOdds),
        (BlockFace.North, SideOdds),
        (BlockFace.South, SideOdds)
    ];

    private static readonly BlockSet SoulFireBase = new("#minecraft:soul_fire_base_blocks");

    /// <summary>
    /// Vanilla's <c>FireBlock.tick</c>: the fire schedules its next tick, then burns out, or ages and spreads.
    /// </summary>
    /// <remarks>
    /// Every write of a fire runs <see cref="OnPlace"/>, which draws the next tick's delay, so the draws stay in
    /// vanilla's order only while the writes do too.
    /// </remarks>
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        level.ScheduleTick(position, block, TickDelay(random));

        // Vanilla's FireBlock.tick runs its body only where ServerLevel.canSpreadFireAround allows it.
        if (level.Level is AbstractLevel concrete && !concrete.CanSpreadFireAround(position))
            return;

        // Vanilla goes on with the tick after putting out a fire that can't survive: it may still age it, which
        // places the fire again, and that placement puts it out again.
        if (!CanSurvive(level.AsShapeLevel(), position))
            level.RemoveBlock(position);

        var below = level.GetBlock(position + BlockFace.Down.ToVector());
        // Fire on an infiniburn block (vanilla's dimension infiniburn tag) never goes out for weather or sturdiness.
        var infiniburn = new BlockSet(level.Level.DimensionType.Element.Infiniburn).Contains(below);
        var age = Age(block);
        var terrain = new MobTerrain(level.Level);

        if (!infiniburn && level.Level.LevelData.Raining && NearRain(terrain, position)
            && random.NextFloat() < 0.2f + age * 0.03f)
        {
            level.RemoveBlock(position);
            return;
        }

        var newAge = Math.Min(MaxAge, age + random.NextInt(3) / 2);
        if (newAge != age)
            level.SetBlock(position, block.WithProperty("age", newAge.ToString()), AgeFlags);

        if (!infiniburn)
        {
            if (!HasBurnableNeighbor(level.AsShapeLevel(), position))
            {
                // Vanilla: with nothing burnable beside it, fire on a sturdy floor goes out past age 3.
                if (!IsSturdy(level.AsShapeLevel(), position + BlockFace.Down.ToVector()) || age > 3)
                    level.RemoveBlock(position);

                return;
            }

            if (age == MaxAge && random.NextInt(4) == 0 && !IsBurnable(below))
            {
                level.RemoveBlock(position);
                return;
            }
        }

        var boost = level.Level.Gameplay(IncreasedFireBurnout, position);
        var burnOutBonus = boost ? -50 : 0;
        foreach (var (face, chance) in BurnOrder)
            Spread(level, position + face.ToVector(), chance + burnOutBonus, random, age);

        IgniteNearby(level, position, age, boost, random, raining: level.Level.LevelData.Raining, terrain);
    }

    /// <summary>
    /// Vanilla's <c>FireBlock.onPlace</c>, after <c>BaseFireBlock</c>'s: a new fire in an empty portal frame lights the
    /// portal, and otherwise goes out if it can't survive; then the fire's tick is scheduled. Every placement draws the
    /// delay from the level's random, even one that lit a portal, went out or whose tick is already scheduled
    /// (scheduling it again does nothing).
    /// </summary>
    public void OnPlace(IBlockLevel level, Vector position, IBlock state, IBlock oldState)
    {
        var newFire = oldState.RegistryId != state.RegistryId;
        if (newFire && !LightsPortal(level, position) && !CanSurvive(level.AsShapeLevel(), position))
            level.RemoveBlock(position);

        level.ScheduleTick(position, state, TickDelay(level.Random));
    }

    // BaseFireBlock.onPlace tries the portal before the fire's survival, so fire in an empty frame stays, even with
    // nothing to hold it, until the level's portal pass replaces it.
    private static bool LightsPortal(IBlockLevel level, Vector position) =>
        level.Level is AbstractLevel concrete && concrete.Portals.QueueFireIgnition(level, position);

    /// <summary>
    /// Vanilla's <c>FireBlock.updateShape</c>: fire that can no longer survive goes out; otherwise its faces are
    /// recomputed for its neighbours.
    /// </summary>
    public IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction, Vector neighborPosition, IBlock neighbor)
    {
        if (!CanSurvive(level, position))
            return BlocksRegistry.Air;

        return StateFor(level, position).WithProperty("age", Age(state).ToString());
    }

    /// <summary>
    /// Whether fire can be set at <paramref name="position"/> (vanilla's <c>BaseFireBlock.canBePlacedAt</c>): the space
    /// is empty and the fire would survive there.
    /// </summary>
    internal static bool CanBePlaced(IBlockLevel level, Vector position) =>
        level.GetBlock(position).IsAir && CanSurvive(level.AsShapeLevel(), position);

    /// <summary>
    /// The state fire takes at <paramref name="position"/> (vanilla's <c>BaseFireBlock.getState</c>): soul fire on a soul
    /// fire base, otherwise fire whose faces are the neighbours it can burn.
    /// </summary>
    internal static IBlock StateFor(IShapeLevel level, Vector position)
    {
        var below = level.GetBlock(position + BlockFace.Down.ToVector());
        if (SoulFireBase.Contains(below))
            return BlocksRegistry.SoulFire;

        // Vanilla's FireBlock.b: a fire on a burnable or sturdy floor has no faces.
        if (IsBurnable(below) || IsSturdy(level, position + BlockFace.Down.ToVector()))
            return BlocksRegistry.Fire;

        var block = BlocksRegistry.Fire;
        foreach (var face in Faces[1..])
            block = block.WithProperty(FaceProperty(face), IsBurnable(level.GetBlock(position + face.ToVector())) ? "true" : "false");

        return block;
    }

    /// <summary>The state of fire with <paramref name="age"/> at <paramref name="position"/>.</summary>
    internal static IBlock StateFor(IShapeLevel level, Vector position, int age)
    {
        var state = StateFor(level, position);
        return state.RegistryId == BlockIds.Fire ? state.WithProperty("age", age.ToString()) : state;
    }

    // Vanilla's BaseFireBlock.canSurvive: a sturdy floor, or a burnable neighbour.
    private static bool CanSurvive(IShapeLevel level, Vector position) =>
        IsSturdy(level, position + BlockFace.Down.ToVector()) || HasBurnableNeighbor(level, position);

    // Vanilla's FireBlock.getIgniteOdds(state) > 0, not counting waterlogged blocks (their odds are 0).
    private static bool IsBurnable(IBlock block) => IgniteOdds(block) > 0;

    private static bool HasBurnableNeighbor(IShapeLevel level, Vector position)
    {
        foreach (var face in Faces)
        {
            if (IsBurnable(level.GetBlock(position + face.ToVector())))
                return true;
        }

        return false;
    }

    private static bool IsSturdy(IShapeLevel level, Vector position) => level.IsFaceSturdy(position, BlockFace.Up);

    // Vanilla's FireBlock.getIgniteOdds(state) and getBurnOdds(state): zero for waterlogged blocks.
    private static int IgniteOdds(IBlock block) =>
        IsWaterlogged(block) ? 0 : BlocksRegistry.GetFireOdds(block.RegistryId)?.Ignite ?? 0;

    private static int BurnOdds(IBlock block) =>
        IsWaterlogged(block) ? 0 : BlocksRegistry.GetFireOdds(block.RegistryId)?.Burn ?? 0;

    private static bool IsWaterlogged(IBlock block) => block.GetProperty("waterlogged") == "true";

    // Vanilla's FireBlock.getIgniteOdds(level, pos): the best ignite odds of the empty space's neighbours.
    private static int IgniteOddsAt(IBlockLevel level, Vector position)
    {
        if (!level.GetBlock(position).IsAir)
            return 0;

        var odds = 0;
        foreach (var face in Faces)
            odds = Math.Max(odds, IgniteOdds(level.GetBlock(position + face.ToVector())));

        return odds;
    }

    // Vanilla's FireBlock.checkBurnOut: a neighbour burns away (the fire takes its place) or catches, by its burn
    // odds out of the chance. The fire placed schedules its own tick (OnPlace).
    private static void Spread(IBlockLevel level, Vector position, int chance, IRandomSource random, int age)
    {
        var replaced = level.GetBlock(position);
        var burn = BurnOdds(replaced);
        if (random.NextInt(chance) >= burn)
            return;

        if (random.NextInt(age + 10) < 5 && !new MobTerrain(level.Level).IsRainingAt(position))
        {
            var newAge = Math.Min(MaxAge, age + random.NextInt(5) / 4);
            level.SetBlock(position, StateFor(level.AsShapeLevel(), position, newAge), BlockUpdateFlags.All);
        }
        else
        {
            level.RemoveBlock(position);
        }

        // Vanilla primes the TNT the fire replaced, after placing the fire; the TNT's push draws from the level's
        // random.
        if (BlocksRegistry.IsTntBlock(replaced.RegistryId) && level.Level is AbstractLevel concrete)
            Entities.PrimedTnt.Prime(concrete, position, owner: null, random: random);
    }

    // Vanilla's FireBlock.tick ignite loop: fire jumps to empty spaces within reach of its own age and the difficulty.
    private static void IgniteNearby(IBlockLevel level, Vector position, int age, bool boost, IRandomSource random, bool raining, MobTerrain terrain)
    {
        var difficulty = (int)level.Level.LevelData.Difficulty;

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
            {
                for (var dy = -1; dy <= 4; dy++)
                {
                    if (dx == 0 && dy == 0 && dz == 0)
                        continue;

                    var bound = 100;
                    if (dy > 1)
                        bound += (dy - 1) * 100;

                    var target = position + new Vector(dx, dy, dz);
                    var odds = IgniteOddsAt(level, target);
                    if (odds <= 0)
                        continue;

                    var chance = (odds + 40 + difficulty * 7) / (age + 30);
                    if (boost)
                        chance /= 2;

                    if (chance <= 0 || random.NextInt(bound) > chance)
                        continue;

                    if (raining && NearRain(terrain, target))
                        continue;

                    // The fire placed schedules its own tick (OnPlace).
                    var newAge = Math.Min(MaxAge, age + random.NextInt(5) / 4);
                    level.SetBlock(target, StateFor(level.AsShapeLevel(), target, newAge), BlockUpdateFlags.All);
                }
            }
        }
    }

    // Vanilla's FireBlock.isNearRain: rain at the position or beside it.
    private static bool NearRain(MobTerrain terrain, Vector position) =>
        terrain.IsRainingAt(position) || terrain.IsRainingAt(position + BlockFace.North.ToVector())
        || terrain.IsRainingAt(position + BlockFace.South.ToVector()) || terrain.IsRainingAt(position + BlockFace.West.ToVector())
        || terrain.IsRainingAt(position + BlockFace.East.ToVector());

    // Vanilla's FireBlock.getFireTickDelay: fire ticks every 30 to 39 ticks.
    private static int TickDelay(IRandomSource random) => 30 + random.NextInt(10);

    private static int Age(IBlock block) =>
        int.TryParse(block.GetProperty("age"), out var age) ? age : 0;

    private static string FaceProperty(BlockFace face) => face switch
    {
        BlockFace.Up => "up",
        BlockFace.North => "north",
        BlockFace.South => "south",
        BlockFace.East => "east",
        BlockFace.West => "west",
        _ => throw new ArgumentOutOfRangeException(nameof(face))
    };
}
