using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

/// <summary>
/// A young mob whose growing up a golden dandelion pauses, like vanilla's <c>AgeableMob</c> and <c>Tadpole</c>.
/// </summary>
internal interface IAgeLockable
{
    /// <summary>Whether the mob's growing up is paused; clients see it (vanilla's <c>AGE_LOCKED</c>).</summary>
    bool AgeLocked { get; }

    /// <summary>Whether a dandelion can lock or unlock it now: it's young (a tadpole always is).</summary>
    bool CanLockAge { get; }

    /// <summary>
    /// The ticks left of the particles the last lock or unlock shows, during which another dandelion does nothing.
    /// </summary>
    int AgeLockTimer { get; set; }

    /// <summary>
    /// Vanilla's <c>setAgeLockedData</c>: locks or unlocks the age, which starts again from the youngest, and starts
    /// the particles.
    /// </summary>
    void ToggleAgeLock();
}

/// <summary>
/// Golden dandelions on young mobs, like vanilla's <c>AgeableMob.canUseGoldenDandelion</c> and <c>setAgeLocked</c>.
/// </summary>
internal static class AgeLocks
{
    /// <summary>How long a lock or unlock shows its particles, and keeps another dandelion from working.</summary>
    public const int ParticleTicks = 40;

    // Vanilla's AGE_LOCK_DOWNWARDS_MOVING_PARTICLE_Y_OFFSET: a lock's particles start a little higher, as they fall.
    private const double LockedParticleLift = 0.2;

    private const int OffhandSlot = 45;

    /// <summary>
    /// A player using a golden dandelion on <paramref name="mob"/>: a young mob that can be locked, past the last
    /// lock's particles, has its age locked or unlocked, which uses up a dandelion and plays the lock's sound to
    /// everyone; a locked mob doesn't despawn any more.
    /// </summary>
    /// <returns>Whether the dandelion was used.</returns>
    public static async ValueTask<bool> TryToggleAsync(Mob mob, IPlayer player, InteractionHand hand)
    {
        if (mob is not IAgeLockable lockable || !mob.Alive || mob.IsRemoved)
            return false;

        var slot = hand == InteractionHand.OffHand ? OffhandSlot : player.CurrentHeldItemSlot;
        var item = player.Inventory.GetItem(slot);
        if (item is not { Count: > 0 } || !item.Is(ItemsRegistry.GoldenDandelion))
            return false;

        var cannot = TagsRegistry.EntityType.CannotBeAgeLocked.Entries.Contains(mob.TypeId);
        if (!lockable.CanLockAge || lockable.AgeLockTimer != 0 || cannot)
            return false;

        lockable.ToggleAgeLock();
        if (player.GameMode != GameMode.Creative)
        {
            player.Inventory.RemoveItem(slot, 1);
            await player.SendInventorySlotAsync(slot);
        }

        if (lockable.AgeLocked)
            mob.PersistenceRequired = true;

        // Vanilla's playSound(null, blockPosition, ...): at the centre of the mob's block, to everyone.
        if (mob.Level is AbstractLevel level)
        {
            var sound = lockable.AgeLocked
                ? "minecraft:item.golden_dandelion.use"
                : "minecraft:item.golden_dandelion.unuse";
            var block = (Vector)mob.Position.Floor();
            level.PacketBroadcaster.BroadcastToLevelInRange(level, block, new SoundPacket
            {
                SoundLocation = sound,
                SoundPosition = new SoundPosition(block.X + 0.5, block.Y + 0.5, block.Z + 0.5),
                Category = SoundCategory.Players,
                Volume = 1,
                Pitch = 1,
                Seed = Globals.Random.NextInt64()
            });
        }

        return true;
    }

    /// <summary>
    /// Vanilla's <c>makeAgeLockedParticle</c>, each tick: while the last lock's timer runs, every other tick shows a
    /// particle over the mob, pausing growth when it's locked and resetting it when it isn't.
    /// </summary>
    public static void Tick(Mob mob, IAgeLockable lockable)
    {
        var timer = lockable.AgeLockTimer;
        if (timer <= 0)
            return;

        if (timer % 2 == 0 && mob.Level is AbstractLevel level)
        {
            // Vanilla's getRandomX(1), getRandomY(0.2) plus the height, getRandomZ(1): somewhere over the mob.
            var locked = lockable.AgeLocked;
            var width = mob.Dimension.Width;
            var height = mob.Dimension.Height;
            var x = mob.Position.X + (mob.Random.NextDouble() * 2 - 1) * width;
            var y = mob.Position.Y + height * mob.Random.NextDouble() * 0.2 + height
                + (locked ? LockedParticleLift : 0);
            var z = mob.Position.Z + (mob.Random.NextDouble() * 2 - 1) * width;
            var particle = locked ? PauseGrowth : ResetGrowth;
            level.SpawnParticles(particle, new VectorD(x, y, z), 1, default, 0);
        }

        lockable.AgeLockTimer = timer - 1;
    }

    private static readonly ParticleData PauseGrowth = new GrowthParticle(ParticleType.PauseMobGrowth);

    private static readonly ParticleData ResetGrowth = new GrowthParticle(ParticleType.ResetMobGrowth);

    private sealed class GrowthParticle(ParticleType type) : ParticleData
    {
        public override ParticleType ParticleType => type;
    }
}
