using Obsidian.API.Blocks;
using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities.AI;
using Obsidian.Registries;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>FireBlock</c> overrides: <c>tick</c> (fire burns out, or spreads to flammable neighbours),
/// <c>updateShape</c> (fire that can't survive goes out), and the state fire is placed in (<see cref="StateFor"/>,
/// vanilla's <c>BaseFireBlock.getState</c>).
/// </summary>
/// <remarks>
/// Flammability is vanilla's <c>FireBlock.bootStrap</c> data, dumped as <c>fire_odds.json</c>. Not ported: the block
/// entity side effect vanilla runs when fire replaces a block (<c>BaseFireBlock</c>'s <c>eis</c> check), and the
/// random draws are not in vanilla's order, so only the odds match.
/// </remarks>
internal sealed partial class FireBlockBehavior : IScheduledTickBehavior, IShapeUpdateBehavior
{
    // Vanilla's FireBlock.MAX_AGE.
    private const int MaxAge = 15;

    // Vanilla's FireBlock.a(...) burn-out and spread: a side neighbour is tried at 300, above and below at 250.
    private const int SideOdds = 300;
    private const int VerticalOdds = 250;

    // Vanilla's EnvironmentAttributes.INCREASED_FIRE_BURNOUT: a bonus to burn-out in the attribute's places.
    private const string IncreasedFireBurnout = LevelEnvironment.IncreasedFireBurnout;

    private static readonly BlockFace[] Faces = [BlockFace.Down, BlockFace.Up, BlockFace.North, BlockFace.South, BlockFace.West, BlockFace.East];

    private static readonly BlockSet SoulFireBase = new("#minecraft:soul_fire_base_blocks");

    /// <summary>Vanilla's <c>FireBlock.tick</c>: the fire burns out, or spreads, and schedules its next tick.</summary>
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        // Vanilla schedules the next tick before anything else, 30 + random(10) ticks on.
        level.ScheduleTick(position, block, 30 + random.NextInt(10));

        if (!CanSurvive(level, position))
        {
            level.RemoveBlock(position);
            return;
        }

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
            level.SetBlock(position, block.WithProperty("age", newAge.ToString()), BlockUpdateFlags.None);

        if (!infiniburn)
        {
            if (!HasBurnableNeighbor(level, position))
            {
                // Vanilla: with nothing burnable beside it, fire on a sturdy floor goes out past age 3.
                if (!IsSturdy(level, position + BlockFace.Down.ToVector()) || age > 3)
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
        Spread(level, position + BlockFace.North.ToVector(), SideOdds + burnOutBonus, random, age);
        Spread(level, position + BlockFace.South.ToVector(), SideOdds + burnOutBonus, random, age);
        Spread(level, position + BlockFace.Down.ToVector(), VerticalOdds + burnOutBonus, random, age);
        Spread(level, position + BlockFace.Up.ToVector(), VerticalOdds + burnOutBonus, random, age);
        Spread(level, position + BlockFace.West.ToVector(), SideOdds + burnOutBonus, random, age);
        Spread(level, position + BlockFace.East.ToVector(), SideOdds + burnOutBonus, random, age);

        IgniteNearby(level, position, age, boost, random, raining: level.Level.LevelData.Raining, terrain);
    }

    /// <summary>
    /// Vanilla's <c>FireBlock.updateShape</c>: fire that can no longer survive goes out; otherwise its faces are
    /// recomputed for its neighbours.
    /// </summary>
    public IBlock? UpdateShape(IBlockLevel level, Vector position, IBlock state, BlockFace direction, Vector neighborPosition, IBlock neighbor)
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
        level.GetBlock(position).IsAir && CanSurvive(level, position);

    /// <summary>
    /// The state fire takes at <paramref name="position"/> (vanilla's <c>BaseFireBlock.getState</c>): soul fire on a soul
    /// fire base, otherwise fire whose faces are the neighbours it can burn.
    /// </summary>
    internal static IBlock StateFor(IBlockLevel level, Vector position)
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
    internal static IBlock StateFor(IBlockLevel level, Vector position, int age)
    {
        var state = StateFor(level, position);
        return state.RegistryId == BlockIds.Fire ? state.WithProperty("age", age.ToString()) : state;
    }

    // Vanilla's BaseFireBlock.canSurvive: a sturdy floor, or a burnable neighbour.
    private static bool CanSurvive(IBlockLevel level, Vector position) =>
        IsSturdy(level, position + BlockFace.Down.ToVector()) || HasBurnableNeighbor(level, position);

    // Vanilla's FireBlock.getIgniteOdds(state) > 0, not counting waterlogged blocks (their odds are 0).
    private static bool IsBurnable(IBlock block) => IgniteOdds(block) > 0;

    private static bool HasBurnableNeighbor(IBlockLevel level, Vector position)
    {
        foreach (var face in Faces)
        {
            if (IsBurnable(level.GetBlock(position + face.ToVector())))
                return true;
        }

        return false;
    }

    private static bool IsSturdy(IBlockLevel level, Vector position) => level.IsFaceSturdy(position, BlockFace.Up);

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

    // Vanilla's FireBlock.a(level, pos, chance, random, age): a neighbour burns away (the fire takes its place) or
    // catches, by its burn odds out of the chance.
    private static void Spread(IBlockLevel level, Vector position, int chance, IRandomSource random, int age)
    {
        var burn = BurnOdds(level.GetBlock(position));
        if (random.NextInt(chance) >= burn)
            return;

        if (random.NextInt(age + 10) < 5 && !new MobTerrain(level.Level).IsRainingAt(position))
        {
            var newAge = Math.Min(MaxAge, age + random.NextInt(5) / 4);
            var fire = StateFor(level, position, newAge);
            level.SetBlock(position, fire, BlockUpdateFlags.All);
            level.ScheduleTick(position, fire, 30 + random.NextInt(10));
        }
        else
        {
            level.RemoveBlock(position);
        }
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

                    var newAge = Math.Min(MaxAge, age + random.NextInt(5) / 4);
                    var fire = StateFor(level, target, newAge);
                    level.SetBlock(target, fire, BlockUpdateFlags.All);
                    level.ScheduleTick(target, fire, 30 + random.NextInt(10));
                }
            }
        }
    }

    // Vanilla's FireBlock.isNearRain: rain at the position or beside it.
    private static bool NearRain(MobTerrain terrain, Vector position) =>
        terrain.IsRainingAt(position) || terrain.IsRainingAt(position + BlockFace.North.ToVector())
        || terrain.IsRainingAt(position + BlockFace.South.ToVector()) || terrain.IsRainingAt(position + BlockFace.West.ToVector())
        || terrain.IsRainingAt(position + BlockFace.East.ToVector());

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
