using System.Threading;
using Obsidian.API.BlockEntities;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.WorldData.Fluids;

namespace Obsidian.WorldData;

/// <summary>
/// A conduit's block entity, like vanilla's <c>ConduitBlockEntity</c>: every 40 game ticks it checks it's surrounded
/// by water and counts the prismarine and sea lanterns of its frame. With 16 or more it's active: it gives Conduit
/// Power to the players in water or rain within 16 blocks for every 7 frame blocks, and with the whole frame of 42 it
/// hurts one hostile mob in water or rain within 8 blocks. Clients get the mob it's hunting.
/// </summary>
internal sealed class ConduitBlockEntity : BlockEntity
{
    // Vanilla's 40-tick refresh, its minimum frame to activate and to hunt, and its hunting reach.
    private const int RefreshInterval = 40;
    private const int MinimumActiveFrame = 16;
    private const int MinimumHuntingFrame = 42;
    private const int HuntingRange = 8;

    // Vanilla's 13 seconds of Conduit Power, given ambient, with particles and an icon.
    private const int EffectDuration = 260;
    private const EntityEffectFlags EffectFlags =
        EntityEffectFlags.IsAmbient | EntityEffectFlags.ShowParticles | EntityEffectFlags.ShowIcon;

    // Vanilla's Enemy mobs: the ones a conduit hunts.
    private static readonly HashSet<EntityType> enemies =
    [
        EntityType.EnderDragon, EntityType.Wither, EntityType.Blaze, EntityType.Creeper, EntityType.ElderGuardian,
        EntityType.Enderman, EntityType.Endermite, EntityType.Ghast, EntityType.Giant, EntityType.Guardian,
        EntityType.Phantom, EntityType.Ravager, EntityType.Shulker, EntityType.Silverfish, EntityType.Vex,
        EntityType.Witch, EntityType.Zoglin, EntityType.Breeze, EntityType.Creaking, EntityType.MagmaCube,
        EntityType.Slime, EntityType.Hoglin, EntityType.Evoker, EntityType.Illusioner, EntityType.Pillager,
        EntityType.Vindicator, EntityType.Piglin, EntityType.PiglinBrute, EntityType.Bogged, EntityType.Parched,
        EntityType.Skeleton, EntityType.Stray, EntityType.WitherSkeleton, EntityType.CaveSpider, EntityType.Spider,
        EntityType.Warden, EntityType.Drowned, EntityType.Husk, EntityType.Zombie, EntityType.ZombieVillager,
        EntityType.ZombifiedPiglin
    ];

    private readonly List<Vector> frame = [];
    private NbtCompound others = new();
    private Guid? target;
    private bool active;
    private long nextAmbientSound;

    internal override bool SavesOnAnyThread => true;

    /// <summary>Whether its frame and water were whole at its last check.</summary>
    public bool Active => this.active;

    /// <summary>The mob it hunts, by UUID, which vanilla saves as <c>Target</c>.</summary>
    public Guid? Target => this.target;

    protected internal override void Load(NbtCompound tag)
    {
        var others = DataBlockEntity.Copy(tag);
        others.Remove("Target");
        this.others = others;

        this.target = tag.TryGetTag<NbtArray<int>>("Target", out var uuid) && uuid.Count == 4
            ? EntityNbt.UuidFromInts(uuid.GetArray())
            : null;
    }

    protected internal override void Save(NbtCompound tag)
    {
        foreach (var (name, child) in DataBlockEntity.Copy(Volatile.Read(ref this.others)))
            tag.Add(name, child);

        if (this.target is Guid hunted)
            tag.Add(new NbtArray<int>("Target", EntityNbt.UuidToInts(hunted)));
    }

    // Vanilla's getUpdateTag, saveCustomOnly: the mob it hunts, which clients draw it attacking.
    protected internal override NbtCompound? GetUpdateData()
    {
        var data = new NbtCompound();
        if (this.target is Guid hunted)
            data.Add(new NbtArray<int>("Target", EntityNbt.UuidToInts(hunted)));

        return data;
    }

    // Vanilla's serverTick.
    protected internal override async ValueTask TickAsync()
    {
        if (this.Level is not AbstractLevel level)
            return;

        var gameTime = level.LevelData.GameTime;
        if (gameTime % RefreshInterval == 0)
            await this.RefreshAsync(level);

        if (!this.active)
            return;

        if (gameTime % (RefreshInterval * 2) == 0)
            LevelSounds.PlayAtBlock(level, this.BlockPosition, "minecraft:block.conduit.ambient");

        if (gameTime > this.nextAmbientSound)
        {
            // Vanilla's draw from the level's random, which the tick shares.
            int delay;
            lock (level.Fluids.TickLock)
                delay = level.GrowthRandom.NextInt(40);

            this.nextAmbientSound = gameTime + 60 + delay;
            LevelSounds.PlayAtBlock(level, this.BlockPosition, "minecraft:block.conduit.ambient.short");
        }
    }

    // The 40-tick check: its shape, the sound of it switching on or off, and while active its powers and its hunt.
    private async ValueTask RefreshAsync(AbstractLevel level)
    {
        var active = this.UpdateFrame(level);
        if (active != this.active)
        {
            var sound = active ? "minecraft:block.conduit.activate" : "minecraft:block.conduit.deactivate";
            LevelSounds.PlayAtBlock(level, this.BlockPosition, sound);
        }

        this.active = active;
        if (!active)
            return;

        this.GivePower(level);
        await this.HuntAsync(level, this.frame.Count >= MinimumHuntingFrame);
    }

    // Vanilla's updateShape: the 3×3×3 around it must all be water; then the frame blocks are counted where its three
    // rings run, the squares two blocks out in each plane through it, in vanilla's x, y, z order.
    private bool UpdateFrame(AbstractLevel level)
    {
        this.frame.Clear();
        var position = this.BlockPosition;

        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                for (var z = -1; z <= 1; z++)
                {
                    var block = level.BlockUpdates.GetBlock(position + new Vector(x, y, z));
                    if (!FluidState.Of(block).IsWater)
                        return false;
                }
            }
        }

        for (var x = -2; x <= 2; x++)
        {
            for (var y = -2; y <= 2; y++)
            {
                for (var z = -2; z <= 2; z++)
                {
                    if (!OnFrame(x, y, z))
                        continue;

                    var at = position + new Vector(x, y, z);
                    var block = level.BlockUpdates.GetBlock(at);
                    if (TagsRegistry.Block.ConduitEffectBlock.Entries.Contains(block.RegistryId))
                        this.frame.Add(at);
                }
            }
        }

        return this.frame.Count >= MinimumActiveFrame;
    }

    // Whether an offset lies on one of the three rings: outside the 3×3×3, in a plane through the conduit, and two
    // blocks out along one of that plane's axes.
    private static bool OnFrame(int x, int y, int z)
    {
        var (ax, ay, az) = (Math.Abs(x), Math.Abs(y), Math.Abs(z));
        if (ax <= 1 && ay <= 1 && az <= 1)
            return false;

        var xPlane = x == 0 && (ay == 2 || az == 2);
        var yPlane = y == 0 && (ax == 2 || az == 2);
        var zPlane = z == 0 && (ax == 2 || ay == 2);
        return xPlane || yPlane || zPlane;
    }

    // Vanilla's applyEffects: 16 blocks of reach for every 7 frame blocks, over a box that reaches up to the top of
    // the world, for the players in water or rain whose block is closer than that.
    private void GivePower(AbstractLevel level)
    {
        var range = this.frame.Count / 7 * 16;
        var position = this.BlockPosition;
        var min = new VectorD(position.X - range, position.Y - range, position.Z - range);
        var max = new VectorD(position.X + 1 + range, position.Y + 1 + range + level.Height, position.Z + 1 + range);
        var terrain = new MobTerrain(level);

        foreach (var player in level.Players.Values.OfType<PlayerBase>())
        {
            // Vanilla's getEntitiesOfClass leaves spectators out.
            if (player.GameMode == GameMode.Spectator)
                continue;

            var box = player.CurrentBox;
            if (!Overlaps(box, min, max))
                continue;

            var block = (Vector)player.Position.Floor();
            if (DistanceSquared(position, block) >= (double)range * range || !InWaterOrRain(terrain, player))
                continue;

            player.AddPotionEffect((int)PotionEffect.ConduitPower - 1, EffectDuration, 0, EffectFlags);
        }
    }

    // Vanilla's updateAndAttackTarget: it keeps its mob while it lives within reach, or picks one; the mob it hunts
    // takes 4 magic damage. Clients are told when the mob changes.
    private async ValueTask HuntAsync(AbstractLevel level, bool hunting)
    {
        var next = hunting ? this.KeptOrChosenTarget(level) : null;
        if (next is not null && level.FindEntity(next.Value) is Living hunted && hunted.Health > 0)
        {
            LevelSounds.Play(level, hunted.Position, "minecraft:block.conduit.attack.target", SoundCategory.Blocks);
            await hunted.DamageCombatAsync(null, 4, CombatDamageKind.Magic);
        }

        if (next != this.target)
        {
            this.target = next;
            this.SetChanged();
            this.SendUpdate();
        }
    }

    // Vanilla's updateDestroyTarget: a mob it already hunts stays while alive and its block within 8; otherwise, with
    // none, one of the hostile mobs in water or rain within 8 of it, picked by the level's random. A lost one leaves
    // it without until the next check.
    private Guid? KeptOrChosenTarget(AbstractLevel level)
    {
        var position = this.BlockPosition;
        if (this.target is Guid current)
        {
            var kept = level.FindEntity(current) is Living { Health: > 0 } living
                && DistanceSquared(position, (Vector)living.Position.Floor()) < HuntingRange * HuntingRange;
            return kept ? current : null;
        }

        var terrain = new MobTerrain(level);
        var min = (VectorD)position - new VectorD(HuntingRange, HuntingRange, HuntingRange);
        var max = (VectorD)position + new VectorD(HuntingRange + 1, HuntingRange + 1, HuntingRange + 1);
        var candidates = EntitiesOverlapping(level, min, max)
            .OfType<Living>()
            .Where(living => living is not PlayerBase && living.Type is EntityType type && enemies.Contains(type))
            .Where(living => InWaterOrRain(terrain, living))
            .ToList();
        if (candidates.Count == 0)
            return null;

        // Vanilla's Util.getRandom, from the level's random.
        int index;
        lock (level.Fluids.TickLock)
            index = level.GrowthRandom.NextInt(candidates.Count);

        return candidates[index].Uuid;
    }

    // Vanilla's isInWaterOrRain: water touching its box, or rain falling on its feet or its head.
    private static bool InWaterOrRain(MobTerrain terrain, Entity entity)
    {
        var box = entity.CurrentBox;
        var margin = new VectorD(0.001, 0.001, 0.001);
        if (terrain.ContainsFluid(new BoundingBox(box.Min + margin, box.Max - margin), water: true))
            return true;

        var feet = (Vector)entity.Position.Floor();
        var head = new Vector(feet.X, (int)Math.Floor(box.Max.Y), feet.Z);
        return terrain.IsRainingAt(feet) || terrain.IsRainingAt(head);
    }

    // The level's entities whose boxes overlap the box from min to max, found within reach of its far corners.
    private static IEnumerable<IEntity> EntitiesOverlapping(AbstractLevel level, VectorD min, VectorD max)
    {
        var center = (min + max) / 2;
        var reach = (float)((max - center).Magnitude + 2);
        return level.GetEntitiesInRange(center, reach)
            .Where(entity => Overlaps(entity is Entity concrete ? concrete.CurrentBox : entity.BoundingBox, min, max));
    }

    private static bool Overlaps(BoundingBox box, VectorD min, VectorD max) =>
        box.Min.X < max.X && box.Max.X > min.X
        && box.Min.Y < max.Y && box.Max.Y > min.Y
        && box.Min.Z < max.Z && box.Max.Z > min.Z;

    private static double DistanceSquared(Vector a, Vector b)
    {
        double x = a.X - b.X;
        double y = a.Y - b.Y;
        double z = a.Z - b.Z;
        return x * x + y * y + z * z;
    }
}
