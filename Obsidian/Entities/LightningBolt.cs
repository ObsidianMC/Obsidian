using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Progress;
using Obsidian.WorldData;

namespace Obsidian.Entities;

/// <summary>
/// A lightning bolt, like vanilla's <c>LightningBolt</c>: on its first tick it sets fire around where it stands (on
/// normal and hard difficulty), powers the lightning rod it strikes and clears the copper around it, then strikes the
/// entities near it every tick it's lit, flashing again up to three times before it goes.
/// </summary>
/// <remarks>
/// Clients play its thunder and impact sounds from the entity's spawn, so the server sends none. A bolt made by a
/// natural strike, <c>/summon</c> or a plugin behaves the same way; one with a <see cref="Cause"/> also fires that
/// player's <c>channeled_lightning</c> trigger for what it strikes. Bolts aren't saved, as in vanilla.
/// </remarks>
[MinecraftEntity("minecraft:lightning_bolt")]
public sealed partial class LightningBolt : Entity
{
    // Vanilla's reaches: entities this far around the bolt (and up to six blocks above that) are struck; the
    // lightning_strike trigger's bystanders are looked for this far around it.
    private const double StrikeReach = 3;
    private const double WitnessReach = 15;
    private const double ExtraHeight = 6;

    // Players this close to the bolt when it goes have their lightning_strike trigger fired.
    private const double TriggerDistance = 256;

    // The level event of the sparks on copper the bolt cleans and on a rod it powers.
    internal const int SparkLevelEvent = 3002;

    private readonly HashSet<IEntity> hitEntities = [];

    // Vanilla's life counts down from 2: the strike happens at 2, and the bolt is lit while it's 0 or more.
    private int life = 2;
    private int flashes;

    /// <summary>
    /// A bolt whose own draws (its seed, how often it flashes and its fire's spots) come from a random seeded at
    /// random, as vanilla's do.
    /// </summary>
    public LightningBolt()
        : this(new LegacyRandomSource(System.Random.Shared.NextInt64()))
    {
    }

    /// <summary>
    /// A bolt whose own draws come from <paramref name="random"/>, which draws its seed and flashes first, as vanilla's
    /// constructor does. Parity replays give it a seeded one.
    /// </summary>
    internal LightningBolt(IRandomSource random)
    {
        Type = EntityType.LightningBolt;
        BoltRandom = random;
        Seed = random.NextLong();
        flashes = random.NextInt(3) + 1;
    }

    /// <summary>The seed clients draw the bolt's shape from; it changes with each flash.</summary>
    public long Seed { get; private set; }

    /// <summary>
    /// Whether the bolt only shows: it sets no fire and strikes no entity, though it still powers a rod, clears copper
    /// and makes its game event, like the bolt of a skeleton trap.
    /// </summary>
    public bool VisualOnly { get; set; }

    /// <summary>The player who called the bolt down, like a Channeling trident's thrower; null for others.</summary>
    public IPlayer? Cause { get; set; }

    /// <summary>How many blocks the bolt set on fire, which the lightning advancement predicate reads.</summary>
    public int BlocksSetOnFire { get; private set; }

    internal IRandomSource BoltRandom { get; }

    // Vanilla's bolt has no size: its box is the point it stands on.
    internal override BoundingBox CurrentBox => new(this.Position, this.Position);

    /// <summary>The entities the bolt has struck that are still alive, as vanilla's <c>getHitEntities</c>.</summary>
    internal IEnumerable<IEntity> HitEntities => this.hitEntities.Where(IsAlive);

    public override async ValueTask TickAsync()
    {
        if (this.Level is not AbstractLevel level)
            return;

        if (this.life == 2)
            this.Strike(level);

        this.life--;
        if (this.life < 0)
        {
            if (this.flashes == 0)
            {
                await this.FireStrikeTriggersAsync(level);
                await this.RemoveAsync();
            }
            else if (this.life < -this.BoltRandom.NextInt(10))
            {
                this.flashes--;
                this.life = 1;
                this.Seed = this.BoltRandom.NextLong();
                this.SpawnFire(level, 0);
            }
        }

        if (this.life >= 0 && !this.VisualOnly)
            await this.StrikeEntitiesAsync();
    }

    // Vanilla's order on the first tick: fire, the rod, the copper, then the game event.
    private void Strike(AbstractLevel level)
    {
        if (level.LevelData.Difficulty is Difficulty.Normal or Difficulty.Hard)
            this.SpawnFire(level, 4);

        var struck = this.StrikePosition;
        LightningRods.Strike(level, struck);
        LightningRods.ClearCopper(level, struck);
        level.EmitGameEvent(MobGameEvent.LightningStrike, this.Position, this);
    }

    // The block the bolt stands on: vanilla takes the block just below its feet.
    private Vector StrikePosition => new(
        (int)Math.Floor(this.Position.X),
        (int)Math.Floor(this.Position.Y - 1.0E-6),
        (int)Math.Floor(this.Position.Z));

    // Vanilla's spawnFire: fire where the bolt stands and at the given number of spots around it, each drawn from the
    // bolt's own random (x, then y, then z), where the space is empty and fire would survive.
    private void SpawnFire(AbstractLevel level, int additionalSources)
    {
        if (this.VisualOnly)
            return;

        var origin = (Vector)this.Position.Floor();
        if (!level.CanSpreadFireAround(origin))
            return;

        var blocks = level.BlockUpdates;
        this.TrySetFire(blocks, origin);
        for (var source = 0; source < additionalSources; source++)
        {
            var dx = this.BoltRandom.NextInt(3) - 1;
            var dy = this.BoltRandom.NextInt(3) - 1;
            var dz = this.BoltRandom.NextInt(3) - 1;
            this.TrySetFire(blocks, origin + new Vector(dx, dy, dz));
        }
    }

    private void TrySetFire(LevelBlockUpdates blocks, Vector position)
    {
        var fire = FireBlockBehavior.StateFor(blocks, position);
        if (!FireBlockBehavior.CanBePlaced(blocks, position))
            return;

        blocks.SetBlock(position, fire);
        this.BlocksSetOnFire++;
    }

    // Every living entity in reach is struck each tick the bolt is lit, in the order they were added; a struck player's
    // channeled_lightning trigger then sees them all.
    private async ValueTask StrikeEntitiesAsync()
    {
        var struck = this.EntitiesAround(StrikeReach).Where(IsAlive).ToList();
        foreach (var entity in struck)
        {
            if (entity is Entity concrete)
                await concrete.ThunderHitAsync(this);
        }

        this.hitEntities.UnionWith(struck);
        if (this.Cause is PlayerBase cause)
            await LightningProgress.FireChanneledAsync(cause, struck);
    }

    // When the bolt goes, players within 256 blocks have their lightning_strike trigger fired, with the living things
    // around it that it didn't strike.
    private async ValueTask FireStrikeTriggersAsync(AbstractLevel level)
    {
        var bystanders = this.EntitiesAround(WitnessReach)
            .Where(entity => IsAlive(entity) && !this.hitEntities.Contains(entity))
            .ToList();
        foreach (var (_, player) in level.Players)
        {
            if (player is PlayerBase concrete && (concrete.Position - this.Position).Magnitude < TriggerDistance)
                await LightningProgress.FireStrikeAsync(concrete, this, bystanders);
        }
    }

    // The entities other than the bolt whose boxes overlap vanilla's box around it: the reach on each side, and six
    // blocks more above. Sorted by when they were added, as vanilla finds them within a section.
    private List<IEntity> EntitiesAround(double reach)
    {
        var min = this.Position - new VectorD(reach, reach, reach);
        var max = this.Position + new VectorD(reach, reach + ExtraHeight, reach);
        var center = (min + max) / 2;
        var radius = (float)((max - min).Magnitude / 2 + 4);
        var found = new List<IEntity>();
        foreach (var entity in this.Level.GetEntitiesInRange(center, radius))
        {
            if (ReferenceEquals(entity, this) || found.Contains(entity))
                continue;

            var box = entity is Entity concrete ? concrete.CurrentBox : entity.BoundingBox;
            if (box.Min.X < max.X && box.Max.X > min.X
                && box.Min.Y < max.Y && box.Max.Y > min.Y
                && box.Min.Z < max.Z && box.Max.Z > min.Z)
            {
                found.Add(entity);
            }
        }

        found.Sort(static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return found;
    }

    // Vanilla's isAlive: not removed, and for living things, with health left.
    private static bool IsAlive(IEntity entity) => entity switch
    {
        Mob mob => !mob.IsRemoved && mob.Health > 0,
        ItemEntity item => !item.Removed,
        Living living => living.Health > 0,
        _ => true
    };
}
