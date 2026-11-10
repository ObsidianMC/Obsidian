using Obsidian.WorldData.BlockNotifications;
using Obsidian.API.Blocks;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using Obsidian.WorldData.Redstone;

namespace Obsidian.Entities;

/// <summary>
/// Where a projectile's move this tick runs into a block: the block, the face it comes through, the point it reaches on
/// that face, and the fraction of the move it travels before it gets there.
/// </summary>
internal readonly record struct ProjectileBlockHit(Vector Block, BlockFace Face, VectorD Location, double Fraction);

/// <summary>
/// The one way every projectile hits a block: vanilla's <c>Projectile.onHitBlock</c>, with the reactions of the
/// blocks a projectile sets off (vanilla's <c>Block.onProjectileHit</c> overrides).
/// </summary>
/// <remarks>
/// <para>
/// A projectile finds the block with <see cref="Find"/>, moves to the hit, and calls <see cref="HitAsync"/> at the
/// point of its tick where vanilla's calls <c>hitTargetOrDeflectSelf</c>. The block reacts first, then the
/// projectile's own block reaction runs (an arrow sticks, a small fireball sets fire, a wind charge bursts), and
/// then <c>projectile_land</c> is emitted at the block, as vanilla's <c>onHit</c> does after <c>onHitBlock</c>. What a
/// projectile does on any hit (a snowball breaking, a fireball exploding) follows once this returns, as vanilla's
/// <c>onHit</c> overrides run after their base.
/// </para>
/// <para>
/// It runs in the level's tick, in the entity pass, so it never waits for a player: the criteria a hit fires are
/// matched now and granted in the server's background work (see <see cref="PlayerBase.QueueProgress"/>). It reads and
/// writes loaded blocks only, so a chunk that unloads before the hit lands is left alone.
/// </para>
/// </remarks>
internal static class ProjectileBlockHits
{
    /// <summary>
    /// The nearest block whose collision shape the move from <paramref name="position"/> by <paramref name="motion"/>
    /// runs into, keeping the block and face even when a shape reaches past its block's cell; null when none.
    /// </summary>
    public static ProjectileBlockHit? Find(MobTerrain terrain, VectorD position, VectorD motion)
    {
        var end = position + motion;
        var min = VectorD.Min(position, end) - new VectorD(0.3f);
        var max = VectorD.Max(position, end) + new VectorD(0.3f);
        var bounds = new BoundingBox(min, max);

        ProjectileBlockHit? nearest = null;
        foreach (var (shape, block) in terrain.GetBlockCollisions(bounds))
        {
            var local = shape.OffsetBy(-(VectorD)block);
            var clipped = BlockShapeRaycast.Clip([local], block, VectorD.Zero, position, motion);
            if (clipped is not { } collision)
                continue;

            if (nearest is not null && collision.Distance >= nearest.Value.Fraction)
                continue;

            var hit = collision.Hit;
            var location = (VectorD)hit.Position + hit.ClickedAt;
            nearest = new ProjectileBlockHit(hit.Position, hit.Face, location, collision.Distance);
        }

        return nearest;
    }

    /// <summary>
    /// Vanilla's <c>onHitBlock</c> and the <c>projectile_land</c> that follows it: the block at the hit reacts, then
    /// <paramref name="ownReaction"/> (the projectile's own block reaction, if it has one) runs, then the game event.
    /// Does nothing for a projectile that has already left <paramref name="level"/>.
    /// </summary>
    /// <param name="level">The level the projectile is ticking in.</param>
    /// <param name="projectile">The projectile, already moved to the hit.</param>
    /// <param name="owner">Who shot or threw it, if anyone.</param>
    /// <param name="hit">The block it hit.</param>
    /// <param name="ownReaction">What the projectile does next, before <c>projectile_land</c>.</param>
    public static async ValueTask HitAsync(AbstractLevel level, Entity projectile, IEntity? owner,
        ProjectileBlockHit hit, Func<ValueTask>? ownReaction = null)
    {
        if (!IsInLevel(level, projectile))
            return;

        if (Loaded(level, hit.Block) is IBlock block)
            await ReactAsync(level, projectile, owner, hit, block);

        if (ownReaction is not null)
            await ownReaction();

        var landed = Loaded(level, hit.Block);
        level.EmitGameEvent(MobGameEvent.ProjectileLand, (VectorD)hit.Block + 0.5, projectile, owner, landed);
    }

    /// <summary>
    /// Whether <paramref name="projectile"/> is on fire, as vanilla's <c>isOnFire</c> is for it: a burning or flaming
    /// arrow, and the blaze's and ghast's fireballs, which set themselves alight every tick. Wither skulls never are.
    /// </summary>
    public static bool IsBurning(Entity projectile) => projectile switch
    {
        WitherSkull => false,
        Arrow arrow => arrow.Burning || arrow.FireSeconds > 0,
        MobProjectile { Type: EntityType.Fireball or EntityType.SmallFireball } => true,
        _ => projectile.Burning
    };

    /// <summary>
    /// Vanilla's <c>Projectile.mayInteract</c>: a player's projectile may (Obsidian has no spawn protection), so may
    /// one with no owner, and a mob's only while <c>mob_griefing</c> is on.
    /// </summary>
    public static bool MayInteract(AbstractLevel level, IEntity? owner) =>
        owner is null or IPlayer || level.LevelData.GetBooleanRule("mob_griefing");

    /// <summary>
    /// Vanilla's <c>Projectile.mayBreak</c>: an impact projectile, while <c>projectiles_can_break_blocks</c> is on,
    /// unless an adventure or spectator player shot it.
    /// </summary>
    public static bool MayBreak(AbstractLevel level, Entity projectile, IEntity? owner) =>
        TagsRegistry.EntityType.ImpactProjectiles.Entries.Contains(projectile.TypeId)
        && level.LevelData.GetBooleanRule("projectiles_can_break_blocks")
        && owner is not IPlayer { GameMode: GameMode.Adventure or GameMode.Spectator };

    // The block's reaction to the hit (vanilla's onProjectileHit overrides). Bells and decorated pots react too, and
    // amethyst chimes and big dripleaves tilt; Obsidian doesn't model those yet (known-gaps/projectile-block-hits.md).
    private static ValueTask ReactAsync(AbstractLevel level, Entity projectile, IEntity? owner,
        ProjectileBlockHit hit, IBlock block) => block.BlockClass() switch
    {
        "TargetBlock" => HitTargetAsync(level, projectile, owner, hit),
        "CampfireBlock" => LightCampfire(level, projectile, owner, hit.Block, block),
        "CandleBlock" or "CandleCakeBlock" => LightCandle(level, projectile, hit.Block, block),
        "TntBlock" => PrimeTntAsync(level, projectile, owner, hit.Block),
        "ChorusFlowerBlock" => BreakChorusFlowerAsync(level, projectile, owner, hit.Block),
        "PointedDripstoneBlock" or "SulfurSpikeBlock" => BreakSpeleothemAsync(level, projectile, owner, hit.Block),
        _ => ValueTask.CompletedTask
    };

    // Vanilla's TargetBlock: power by how near the centre of the face the hit is, and a player's hit counts towards
    // target_hit, with that strength, even while an earlier pulse keeps the power.
    private static async ValueTask HitTargetAsync(AbstractLevel level, Entity projectile, IEntity? owner,
        ProjectileBlockHit hit)
    {
        // Shulker bullets and wither skulls are arrows to Obsidian, but not AbstractArrows to vanilla.
        var arrow = projectile is Arrow and not (ShulkerBullet or WitherSkull);
        var pulse = arrow ? TargetBlock.ArrowPulseTicks : TargetBlock.PulseTicks;
        var strength = await level.RedstoneInstall.Targets.HitAsync(hit.Block, hit.Location, hit.Face, pulse);
        if (owner is not PlayerBase player)
            return;

        player.Statistics.Increment("minecraft:target_hit");
        player.QueueProgress("target_hit", entity: projectile, signal: strength);
    }

    // Vanilla's CampfireBlock: a burning projectile that may interact lights an unlit, dry campfire.
    private static ValueTask LightCampfire(AbstractLevel level, Entity projectile, IEntity? owner, Vector position,
        IBlock block)
    {
        if (IsBurning(projectile) && MayInteract(level, owner) && BlockLighting.CanLight(block))
            level.BlockUpdates.SetBlock(position, block.WithProperty("lit", true), BlockUpdateFlags.AllImmediate);

        return ValueTask.CompletedTask;
    }

    // Vanilla's AbstractCandleBlock: a burning projectile lights a candle or candle cake that can be lit, whoever shot
    // it.
    private static ValueTask LightCandle(AbstractLevel level, Entity projectile, Vector position, IBlock block)
    {
        if (IsBurning(projectile) && BlockLighting.CanLight(block))
            level.BlockUpdates.SetBlock(position, block.WithProperty("lit", true), BlockUpdateFlags.AllImmediate);

        return ValueTask.CompletedTask;
    }

    // Vanilla's TntBlock: a burning projectile that may interact primes it, crediting a living owner, and the block
    // goes once the lit TNT is out. An adventure player's projectile can't prime it.
    private static ValueTask PrimeTntAsync(AbstractLevel level, Entity projectile, IEntity? owner, Vector position)
    {
        if (!IsBurning(projectile) || !MayInteract(level, owner))
            return ValueTask.CompletedTask;

        if (owner is IPlayer { GameMode: GameMode.Adventure })
            return ValueTask.CompletedTask;

        var lighter = owner as Living;
        if (PrimedTnt.Prime(level, position, lighter) is null)
            return ValueTask.CompletedTask;

        level.EmitGameEvent(MobGameEvent.PrimeFuse, (VectorD)position + 0.5, lighter);
        level.BlockUpdates.SetBlock(position, BlocksRegistry.Air, BlockUpdateFlags.All);
        return ValueTask.CompletedTask;
    }

    // Vanilla's ChorusFlowerBlock: an impact projectile that may interact and break breaks it, with its drop.
    private static ValueTask BreakChorusFlowerAsync(AbstractLevel level, Entity projectile, IEntity? owner,
        Vector position)
    {
        if (!MayInteract(level, owner) || !MayBreak(level, projectile, owner))
            return ValueTask.CompletedTask;

        return level.DestroyBlockInTickAsync(position, breaker: projectile);
    }

    // Vanilla's SpeleothemBlock: a trident fast enough that may interact and break breaks pointed dripstone or a
    // sulfur spike, with its drop, crediting no one.
    private static ValueTask BreakSpeleothemAsync(AbstractLevel level, Entity projectile, IEntity? owner,
        Vector position)
    {
        if (projectile is not Trident)
            return ValueTask.CompletedTask;

        var allowed = MayInteract(level, owner) && MayBreak(level, projectile, owner);
        if (!SulfurSpikeBehavior.BreaksFromTrident(projectile.Motion.Magnitude, allowed))
            return ValueTask.CompletedTask;

        return level.DestroyBlockInTickAsync(position, breaker: null);
    }

    /// <summary>
    /// A projectile's sound at <paramref name="position"/>, for the players around it (vanilla's
    /// <c>Entity.playSound</c>, in the neutral category).
    /// </summary>
    public static void PlaySound(AbstractLevel level, VectorD position, string sound, float volume, float pitch)
    {
        level.PacketBroadcaster.BroadcastToLevelInRange(level, (Vector)position.Floor(), new SoundPacket
        {
            SoundLocation = sound,
            SoundPosition = new SoundPosition(position.X, position.Y, position.Z),
            Category = SoundCategory.Neutral,
            Volume = volume,
            Pitch = pitch,
            Seed = Globals.Random.NextInt64()
        });
    }

    // Whether the projectile is still one of the level's entities: a projectile removed or moved elsewhere during its
    // tick hits nothing.
    private static bool IsInLevel(AbstractLevel level, Entity projectile) =>
        projectile.Level == level
        && level.GetRegionForLocation(projectile.Position)?.Entities.ContainsKey(projectile.EntityId) == true;

    private static IBlock? Loaded(AbstractLevel level, Vector position) =>
        ((IBlockChangeWorld)level).GetLoadedBlock(position);
}
