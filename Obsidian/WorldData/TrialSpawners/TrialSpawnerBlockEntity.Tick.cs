using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.WorldData.Dispensing;

namespace Obsidian.WorldData.TrialSpawners;

internal sealed partial class TrialSpawnerBlockEntity
{
    // Vanilla's level events: a spawner detecting players (normal and ominous), the flames as a mob spawns at the
    // spawner and where the mob appears, and the ejection of a reward.
    private const int DetectPlayersEvent = 3013;
    private const int DetectPlayersOminousEvent = 3019;
    private const int SpawnerFlamesEvent = 3011;
    private const int MobFlamesEvent = 3012;
    private const int EjectRewardEvent = 3014;

    // Vanilla's TrialSpawner and TrialSpawnerState constants.
    private const int PlayerScanInterval = 20;
    private const int DetectedPlayerSpawnDelay = 40;
    private const int MaxMobTrackingDistanceSquared = 47 * 47;
    private const float DelayBeforeEjecting = 40f;
    private const float TicksBetweenEjections = 30f;

    // Rewards drop from 1.2 blocks above the spawner's bottom centre, spread as a dispenser's with accuracy 2.
    private const double EjectionHeight = 1.2;
    private const int EjectionAccuracy = 2;

    // Whether the spawn data's mob has loaded, as vanilla's display entity: once it has, the spawner never goes back
    // to waiting for it.
    private bool hasDisplayEntity;

    /// <summary>
    /// Lets the spawner spawn whatever the difficulty and the <c>spawn_mobs</c> rule say, like vanilla's
    /// <c>overridePeacefulAndMobSpawnRule</c>, which its tests use. Replays set it to keep natural spawning off.
    /// </summary>
    internal bool OverridesSpawnRules { get; set; }

    protected internal override ValueTask TickAsync()
    {
        if (this.Level is AbstractLevel level)
        {
            lock (level.Fluids.TickLock)
                this.Tick(level);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// One tick of the spawner's trial, like vanilla's <c>TrialSpawner.tickServer</c>: it lets go of mobs that are
    /// gone, then moves its state on. Call holding the level's tick lock.
    /// </summary>
    internal void Tick(AbstractLevel level)
    {
        var block = level.BlockUpdates.GetBlock(this.BlockPosition);
        if (!block.Is(BlockIds.TrialSpawner))
            return;

        var trial = new Trial(this, level, block);
        this.State = trial.State;

        var kept = trial.Data.CurrentMobs.RemoveAll(uuid => ShouldUntrack(level, this.BlockPosition, uuid));
        if (kept.Count != trial.Data.CurrentMobs.Count)
        {
            trial.Data = trial.Data with
            {
                CurrentMobs = kept,
                NextMobSpawnsAt = trial.Time + trial.Config.TicksBetweenSpawn
            };
        }

        var next = trial.State switch
        {
            TrialSpawnerState.Inactive => this.TickInactive(trial),
            TrialSpawnerState.WaitingForPlayers => this.TickWaitingForPlayers(trial),
            TrialSpawnerState.Active => this.TickActive(trial),
            TrialSpawnerState.WaitingForRewardEjection => TickWaitingForRewardEjection(trial),
            TrialSpawnerState.EjectingReward => TickEjectingReward(trial),
            _ => this.TickCooldown(trial)
        };

        trial.Publish();
        if (next != trial.State)
            this.SetState(trial, next);
    }

    // A mob the spawner stops tracking, like vanilla's shouldMobBeUntracked: gone, dead, in another level, or more
    // than 47 blocks from the spawner.
    private static bool ShouldUntrack(AbstractLevel level, Vector spawner, Guid uuid)
    {
        if (level.FindEntity(uuid) is not Entity entity)
            return true;

        var alive = entity switch
        {
            Mob mob => !mob.IsRemoved && mob.Alive,
            Living living => living.Alive,
            _ => true
        };
        var offset = (Vector)entity.Position.Floor() - spawner;
        var distance = (long)offset.X * offset.X + (long)offset.Y * offset.Y + (long)offset.Z * offset.Z;
        return !alive || entity.Level != level || distance > MaxMobTrackingDistanceSquared;
    }

    private TrialSpawnerState TickInactive(Trial trial) =>
        this.HasDisplayEntity(trial) ? TrialSpawnerState.WaitingForPlayers : TrialSpawnerState.Inactive;

    private TrialSpawnerState TickWaitingForPlayers(Trial trial)
    {
        if (!this.CanSpawnInLevel(trial.Level))
        {
            trial.Data = ResetStatistics(trial.Data);
            return trial.State;
        }

        if (!this.HasMobToSpawn(trial))
            return TrialSpawnerState.Inactive;

        this.TryDetectPlayers(trial);
        return trial.Data.RegisteredPlayers.IsEmpty ? trial.State : TrialSpawnerState.Active;
    }

    private TrialSpawnerState TickActive(Trial trial)
    {
        if (!this.CanSpawnInLevel(trial.Level))
        {
            trial.Data = ResetStatistics(trial.Data);
            return TrialSpawnerState.WaitingForPlayers;
        }

        if (!this.HasMobToSpawn(trial))
            return TrialSpawnerState.Inactive;

        // Counted before this tick's detection, like vanilla's.
        var additionalPlayers = Math.Max(0, trial.Data.RegisteredPlayers.Count - 1);
        this.TryDetectPlayers(trial);

        var config = trial.Config;
        if (trial.Data.TotalMobsSpawned >= config.TargetTotalMobs(additionalPlayers))
        {
            if (!trial.Data.CurrentMobs.IsEmpty)
                return trial.State;

            trial.Data = trial.Data with
            {
                CooldownEndsAt = trial.Time + trial.Data.TargetCooldownLength,
                TotalMobsSpawned = 0,
                NextMobSpawnsAt = 0
            };
            return TrialSpawnerState.WaitingForRewardEjection;
        }

        var ready = trial.Time >= trial.Data.NextMobSpawnsAt
            && trial.Data.CurrentMobs.Count < config.TargetSimultaneousMobs(additionalPlayers);
        if (!ready || this.SpawnMob(trial) is not Guid spawned)
            return trial.State;

        trial.Data = trial.Data with
        {
            CurrentMobs = trial.Data.CurrentMobs.Add(spawned),
            TotalMobsSpawned = trial.Data.TotalMobsSpawned + 1,
            NextMobSpawnsAt = trial.Time + config.TicksBetweenSpawn
        };

        // The next mob's spawn data, drawn after each spawn.
        if (config.SpawnPotentials.TryPick(trial.Random, out var nextSpawnData))
        {
            trial.Data = trial.Data with { NextSpawnData = nextSpawnData };
            trial.MarkUpdated();
        }

        return trial.State;
    }

    private static TrialSpawnerState TickWaitingForRewardEjection(Trial trial)
    {
        // Vanilla compares in single precision.
        var cooldownStartedAt = trial.Data.CooldownEndsAt - trial.Data.TargetCooldownLength;
        if ((float)trial.Time < (float)cooldownStartedAt + DelayBeforeEjecting)
            return trial.State;

        PlaySound(trial, SoundId.Block.TrialSpawnerOpenShutter);
        return TrialSpawnerState.EjectingReward;
    }

    private static TrialSpawnerState TickEjectingReward(Trial trial)
    {
        var cooldownStartedAt = trial.Data.CooldownEndsAt - trial.Data.TargetCooldownLength;
        if ((float)(trial.Time - cooldownStartedAt) % TicksBetweenEjections != 0f)
            return trial.State;

        if (trial.Data.RegisteredPlayers.IsEmpty)
        {
            PlaySound(trial, SoundId.Block.TrialSpawnerCloseShutter);
            trial.Data = trial.Data with { EjectingLootTable = null };
            return TrialSpawnerState.Cooldown;
        }

        // One loot table for the whole ejection, picked for its first reward.
        if (trial.Data.EjectingLootTable is null
            && trial.Config.LootTablesToEject.TryPick(trial.Random, out var picked))
        {
            trial.Data = trial.Data with { EjectingLootTable = picked };
        }

        if (trial.Data.EjectingLootTable is string table)
            EjectReward(trial, table);

        // One reward for each player: the first in the set's order goes.
        trial.Data = trial.Data with { RegisteredPlayers = trial.Data.RegisteredPlayers.RemoveFirst() };
        return trial.State;
    }

    private TrialSpawnerState TickCooldown(Trial trial)
    {
        this.TryDetectPlayers(trial);
        if (!trial.Data.RegisteredPlayers.IsEmpty)
        {
            trial.Data = trial.Data with { TotalMobsSpawned = 0, NextMobSpawnsAt = 0 };
            return TrialSpawnerState.Active;
        }

        if (trial.Time < trial.Data.CooldownEndsAt)
            return trial.State;

        // Vanilla's removeOminous, which sets the block back to normal whether or not it was ominous.
        var normal = trial.Level.BlockUpdates.GetBlock(this.BlockPosition).WithProperty("ominous", false);
        trial.Level.BlockUpdates.SetBlock(this.BlockPosition, normal);
        trial.Data = ResetStatistics(trial.Data with
        {
            CurrentMobs = trial.Data.CurrentMobs.Clear(),
            NextSpawnData = null
        });
        return TrialSpawnerState.WaitingForPlayers;
    }

    private static TrialSpawnerData ResetStatistics(TrialSpawnerData data) => data with
    {
        RegisteredPlayers = data.RegisteredPlayers.Clear(),
        TotalMobsSpawned = 0,
        NextMobSpawnsAt = 0,
        CooldownEndsAt = 0
    };

    // Vanilla's canSpawnInLevel: spawner blocks work, and mobs spawn there (or the test override says they do).
    private bool CanSpawnInLevel(AbstractLevel level)
    {
        var rules = level.LevelData;
        if (!rules.GetBooleanRule("spawner_blocks_work"))
            return false;

        if (this.OverridesSpawnRules)
            return true;

        return rules.Difficulty != Difficulty.Peaceful && rules.GetBooleanRule("spawn_mobs");
    }

    // Vanilla's hasMobToSpawn: the next spawn data names a mob, or the config could pick another.
    private bool HasMobToSpawn(Trial trial)
    {
        var spawnData = this.GetOrPickNextSpawnData(trial);
        return HasEntityId(spawnData) || !trial.Config.SpawnPotentials.IsEmpty;
    }

    // Vanilla's getOrCreateDisplayEntity, for the spinning mob of a waiting spawner: the spawn data's mob, once it
    // names one this server can make.
    private bool HasDisplayEntity(Trial trial)
    {
        if (this.hasDisplayEntity)
            return true;

        var spawnData = this.GetOrPickNextSpawnData(trial);
        if (!spawnData.TryGetTag<NbtCompound>("entity", out var entity)
            || !entity.TryGetTagValue<string>("id", out var id))
        {
            return false;
        }

        this.hasDisplayEntity = EntityNbt.IsKnownType(id) || EntityDefinitionsRegistry.TryGet(id, out _);
        return this.hasDisplayEntity;
    }

    /// <summary>
    /// The next mob's spawn data, picked from the active config the first time it's asked for, like vanilla's
    /// <c>getOrCreateNextSpawnData</c>: one draw, or none when the config has nothing to pick, which leaves empty spawn
    /// data. Clients are told.
    /// </summary>
    private NbtCompound GetOrPickNextSpawnData(Trial trial)
    {
        if (trial.Data.NextSpawnData is NbtCompound current)
            return current;

        var picked = trial.Config.SpawnPotentials.TryPick(trial.Random, out var spawnData)
            ? spawnData
            : new NbtCompound { new NbtCompound("entity") };
        trial.Data = trial.Data with { NextSpawnData = picked };
        trial.MarkUpdated();
        return picked;
    }

    private static bool HasEntityId(NbtCompound spawnData) =>
        spawnData.TryGetTag<NbtCompound>("entity", out var entity) && entity.TryGetTagValue<string>("id", out _);

    /// <summary>
    /// Vanilla's <c>TrialSpawnerStateData.tryDetectPlayers</c>: once every 20 ticks, staggered by position, the
    /// players in range join the trial. The first must be in the spawner's line of sight; once one has joined, the
    /// others join from anywhere in range. In a cooldown, nobody joins.
    /// </summary>
    private void TryDetectPlayers(Trial trial)
    {
        // Vanilla's BlockPos.asLong plus the game time, with Java's remainder.
        var position = this.BlockPosition;
        if ((PackedPosition(position) + trial.Time) % PlayerScanInterval != 0)
            return;

        if (trial.State == TrialSpawnerState.Cooldown && trial.Ominous)
            return;

        var inSight = this.DetectPlayers(trial, requireLineOfSight: true);

        // Becoming ominous (a player with Bad Omen or Trial Omen) is left for ominous trials.
        if (trial.State == TrialSpawnerState.Cooldown)
            return;

        var found = trial.Data.RegisteredPlayers.IsEmpty
            ? inSight
            : this.DetectPlayers(trial, requireLineOfSight: false);
        var registered = trial.Data.RegisteredPlayers.AddRange(found);
        if (ReferenceEquals(registered, trial.Data.RegisteredPlayers))
            return;

        trial.Data = trial.Data with
        {
            RegisteredPlayers = registered,
            NextMobSpawnsAt = Math.Max(trial.Time + DetectedPlayerSpawnDelay, trial.Data.NextMobSpawnsAt)
        };
        var levelEvent = trial.Ominous ? DetectPlayersOminousEvent : DetectPlayersEvent;
        trial.Level.BroadcastLevelEvent(levelEvent, position, registered.Count);
    }

    // Vanilla's PlayerDetector.NO_CREATIVE_PLAYERS: players but creative and spectators whose block is within the
    // range of the spawner's block, and, if asked, that the spawner's centre can see from their eyes.
    private List<Guid> DetectPlayers(Trial trial, bool requireLineOfSight)
    {
        var center = (VectorD)this.BlockPosition + 0.5;
        var range = (double)trial.Data.RequiredPlayerRange;
        var terrain = new MobTerrain(trial.Level);
        var found = new List<Guid>();
        foreach (var player in trial.Level.Players.Values)
        {
            if (player.GameMode is GameMode.Creative or GameMode.Spectator)
                continue;

            var offset = (Vector)player.Position.Floor() - this.BlockPosition;
            var distance = (double)offset.X * offset.X + (double)offset.Y * offset.Y + (double)offset.Z * offset.Z;
            if (distance >= range * range)
                continue;

            if (requireLineOfSight && !InLineOfSight(terrain, center, PlayerBase.EyePosition(player)))
                continue;

            found.Add(player.Uuid);
        }

        return found;
    }

    // Vanilla's inLineOfSight: a visual-shape clip from the far end back to the spawner's centre that reaches the
    // spawner's own block, or hits nothing. Vanilla's clip would load a chunk on the way that isn't loaded and look
    // through it; that can't be waited for here, so a line through one counts as blocked until it's loaded.
    private static bool InLineOfSight(MobTerrain terrain, VectorD spawnerCenter, VectorD from)
    {
        var hit = terrain.RaycastVisual(from, spawnerCenter, out var unloaded);
        return !unloaded && (hit is null || hit.Value.Position == (Vector)spawnerCenter.Floor());
    }

    /// <summary>
    /// Vanilla's <c>TrialSpawner.spawnMob</c>: a mob from the next spawn data, somewhere around the spawner it can see
    /// and fits, like the spawn data's mob, which then must persist.
    /// </summary>
    /// <remarks>
    /// Draws from the level's random in vanilla's order, as the draws decide where mobs appear and what comes next:
    /// the spawn data (when none is picked yet), the position (two doubles for x, an int for y, two doubles for z),
    /// the yaw once the position passed its checks, then a bare mob's spawn setup.
    /// </remarks>
    /// <returns>The mob's UUID, or null when it didn't spawn.</returns>
    private Guid? SpawnMob(Trial trial)
    {
        var level = trial.Level;
        var random = trial.Random;
        var spawnData = this.GetOrPickNextSpawnData(trial);
        if (!spawnData.TryGetTag<NbtCompound>("entity", out var entityData)
            || !entityData.TryGetTagValue<string>("id", out var id)
            || EntityNbt.Load(new NbtCompound { new NbtTag<string>("id", id) }, level) is not Entity sample)
        {
            return null;
        }

        var spawner = this.BlockPosition;
        var position = EntityNbt.TryReadVector(entityData, "Pos", out var fixedPosition)
            ? fixedPosition
            : RandomSpawnPosition(random, spawner, trial.Config.SpawnRange);

        var terrain = new MobTerrain(level);
        if (!terrain.IsFree(sample.Dimension.CreateBBFromPosition(position)))
            return null;

        if (!InLineOfSight(terrain, (VectorD)spawner + 0.5, position))
            return null;

        var blockPosition = (Vector)position.Floor();
        if (!TrialSpawnRules.Allow(level, sample, blockPosition))
            return null;

        if (spawnData.TryGetTag<NbtCompound>("custom_spawn_rules", out var rules)
            && !TrialSpawnRules.FitsLightLimits(level, blockPosition, rules))
        {
            return null;
        }

        var entity = EntityNbt.Load(entityData, level);
        if (entity is null)
            return null;

        entity.Position = position;
        entity.Yaw = random.NextFloat() * 360f;
        entity.Pitch = 0;

        var companions = default(SpawnCompanions);
        if (entity is Mob mob)
        {
            if (!TrialSpawnRules.IsUnobstructed(level, mob))
                return null;

            // Spawn data that only names the mob gets its natural spawn setup, from the level's random as vanilla's
            // finalizeSpawn draws it; any more and it's taken as given. Loading the mob already set up its AI.
            var bare = entityData.Count == 1;
            if (bare)
                companions = mob.FinalizeBareSpawn(level, random);

            mob.PersistenceRequired = true;
        }

        // A jockey the setup made: vanilla adds a baby zombie's chicken during its setup, before the mob, and a
        // spider's skeleton with it, after it (addFreshEntityWithPassengers).
        if (companions.Vehicle is Mob vehicle && !level.TrySpawnEntity(vehicle, hostOwned: true))
            companions = companions with { Vehicle = null };

        if (!level.TrySpawnEntity(entity, hostOwned: true))
            return null;

        if (companions.Vehicle is Mob ridden)
            entity.StartRiding(ridden);

        if (companions.Passenger is Mob passenger && level.TrySpawnEntity(passenger, hostOwned: true))
            passenger.StartRiding(entity);

        var flames = trial.Ominous ? 1 : 0;
        level.BroadcastLevelEvent(SpawnerFlamesEvent, spawner, flames);
        level.BroadcastLevelEvent(MobFlamesEvent, blockPosition, flames);
        level.EmitGameEvent(MobGameEvent.EntityPlace, (VectorD)blockPosition + 0.5, entity);
        return entity.Uuid;
    }

    // Vanilla's spawn position: within the spawn range along x and z (a triangular spread), and from a block below to
    // a block above the spawner. Java evaluates the draws left to right, so this order is vanilla's.
    private static VectorD RandomSpawnPosition(IRandomSource random, Vector spawner, int range)
    {
        var x = spawner.X + (random.NextDouble() - random.NextDouble()) * range + 0.5;
        var y = spawner.Y + random.NextInt(3) - 1;
        var z = spawner.Z + (random.NextDouble() - random.NextDouble()) * range + 0.5;
        return new VectorD(x, y, z);
    }

    /// <summary>
    /// Vanilla's <c>TrialSpawner.ejectReward</c>: the loot table's items, each tossed up from the top of the spawner
    /// as a dispenser tosses items, then the ejection's level event when there were any. The table rolls with its
    /// random sequence, or the level's random without one.
    /// </summary>
    private static void EjectReward(Trial trial, string tableId)
    {
        var level = trial.Level;
        var resolver = PluginLootTables.CaptureResolver();
        if (resolver(tableId) is not LootTable table)
            return;

        var random = table.RandomSequence is string sequence
            ? BlockLoot.SequencesFor(level).Get(sequence)
            : level.GrowthRandom;
        var spawner = trial.Entity.BlockPosition;
        List<ItemStack> drops;
        lock (random)
        {
            drops = table.GetRandomItems(new LootContext
            {
                EntityVillagerVariant = LootEntityComponents.VillagerVariant,
                Random = random,
                ResolveTable = resolver
            });
        }

        if (drops.Count == 0)
            return;

        var from = new VectorD(spawner.X + 0.5, spawner.Y + EjectionHeight, spawner.Z + 0.5);
        foreach (var drop in drops)
            DispenseBehaviors.SpawnItem(level, drop, EjectionAccuracy, BlockFace.Up, from);

        level.BroadcastLevelEvent(EjectRewardEvent, spawner, 0);
    }

    private static void PlaySound(Trial trial, string sound) =>
        trial.Level.BlockUpdates.PlaySound(trial.Entity.BlockPosition, "minecraft:" + sound);

    // Vanilla's setState: the block's state property, and clients get the block entity's data with it.
    private void SetState(Trial trial, TrialSpawnerState next)
    {
        var current = trial.Level.BlockUpdates.GetBlock(this.BlockPosition);
        if (!current.Is(BlockIds.TrialSpawner))
            return;

        this.State = next;
        this.SetChanged();
        var updated = current.WithProperty(TrialSpawnerStates.Property, next.Name());
        trial.Level.BlockUpdates.SetBlock(this.BlockPosition, updated);
        this.SendUpdate();
    }

    // Vanilla's BlockPos.asLong: 26 bits of x, 26 of z and 12 of y.
    private static long PackedPosition(Vector position) =>
        ((long)position.X & 0x3FFFFFF) << 38 | ((long)position.Z & 0x3FFFFFF) << 12 | ((long)position.Y & 0xFFF);

    /// <summary>One tick's view of the spawner: its level, block state and data, which the tick replaces.</summary>
    private sealed class Trial(TrialSpawnerBlockEntity entity, AbstractLevel level, IBlock block)
    {
        private bool changed;

        public TrialSpawnerBlockEntity Entity { get; } = entity;

        public AbstractLevel Level { get; } = level;

        public TrialSpawnerState State { get; } = TrialSpawnerStates.Of(block);

        public bool Ominous { get; } = block.GetProperty("ominous") == "true";

        public long Time { get; } = level.LevelData.GameTime;

        public IRandomSource Random => this.Level.GrowthRandom;

        public TrialSpawnerData Data
        {
            get => field;
            set
            {
                if (field == value)
                    return;

                field = value;
                this.changed = true;
            }
        } = entity.Data;

        /// <summary>The config in use: the ominous one in an ominous trial.</summary>
        public TrialSpawnerConfig Config => this.Ominous ? this.Data.OminousConfig : this.Data.NormalConfig;

        /// <summary>Vanilla's <c>markUpdated</c>: saved, and clients told, at the end of the tick.</summary>
        public void MarkUpdated()
        {
            this.Entity.SetChanged();
            this.Entity.SendUpdate();
        }

        /// <summary>Hands the tick's data to saves.</summary>
        public void Publish()
        {
            if (!this.changed)
                return;

            this.Entity.Data = this.Data;
            this.Entity.SetChanged();
        }
    }
}
