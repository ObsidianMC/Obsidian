using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.Entities.AI;

namespace Obsidian.WorldData.TrialSpawners;

internal sealed partial class TrialSpawnerBlockEntity
{
    // Transient, as in vanilla: the position-seeded loot is cached until this block entity is loaded again.
    private WeightedEntries<ItemStack>? ominousItems;

    private bool TryBecomeOminous(Trial trial, List<Guid> detected)
    {
        PlayerBase? chosen = null;
        foreach (var uuid in detected)
        {
            if (trial.Level.FindEntity(uuid) is not PlayerBase player)
                continue;

            if (player.HasPotionEffect((int)PotionEffect.TrialOmen - 1))
            {
                chosen = player;
                break;
            }

            if (player.HasPotionEffect((int)PotionEffect.BadOmen - 1))
                chosen = player;
        }

        if (chosen is null)
            return false;

        chosen.ConvertBadOmenForTrial();
        trial.Level.BroadcastLevelEvent(3020, (Vector)PlayerBase.EyePosition(chosen).Floor(), 0);
        var block = trial.Level.BlockUpdates.GetBlock(this.BlockPosition).WithProperty("ominous", true);
        trial.Level.BlockUpdates.SetBlock(this.BlockPosition, block);
        trial.Level.BroadcastLevelEvent(3020, this.BlockPosition, 1);
        trial.Ominous = true;

        foreach (var uuid in trial.Data.CurrentMobs.InIterationOrder)
        {
            if (trial.Level.FindEntity(uuid) is not Entity mob)
                continue;

            trial.Level.BroadcastLevelEvent(MobFlamesEvent, (Vector)mob.Position.Floor(), 0);
            if (mob is Mob equipped)
                equipped.DropPreservedEquipment();

            // Built-in entity removal completes synchronously; it takes TransferLock before chunk and grid locks.
            // Calling the entity override also retires its AI and passengers.
            var removal = mob.RemoveAsync();
            if (!removal.IsCompletedSuccessfully)
                trial.Level.EnqueueEntityAction(async () => await removal);
        }

        trial.Data = trial.Data with
        {
            CurrentMobs = trial.Data.CurrentMobs.Clear(),
            TotalMobsSpawned = 0,
            NextSpawnData = trial.Data.OminousConfig.SpawnPotentials.IsEmpty ? trial.Data.NextSpawnData : null,
            NextMobSpawnsAt = trial.Time + trial.Data.OminousConfig.TicksBetweenSpawn,
            CooldownEndsAt = trial.Time + TrialSpawnerConfig.TicksBetweenItemSpawners
        };
        trial.MarkUpdated();
        return true;
    }

    private void TrySpawnOminousItem(Trial trial)
    {
        // Vanilla selects an item on every active tick, even before the timer is due. Its weight is its loot count.
        var items = this.OminousItems(trial);
        if (!items.TryPick(trial.Random, out var item) || item.IsAir || trial.Time < trial.Data.CooldownEndsAt)
            return;

        var center = (VectorD)this.BlockPosition + 0.5;
        var rangeSquared = trial.Data.RequiredPlayerRange * trial.Data.RequiredPlayerRange;
        var players = trial.Data.RegisteredPlayers.InIterationOrder
            .Select(trial.Level.FindEntity)
            .OfType<PlayerBase>()
            .Where(player => player.Alive && player.GameMode is not (GameMode.Creative or GameMode.Spectator)
                && (player.Position - center).MagnitudeSquared() <= rangeSquared)
            .Cast<Entity>()
            .ToList();
        if (players.Count == 0)
            return;

        var targets = trial.Random.NextBoolean()
            ? trial.Data.CurrentMobs.InIterationOrder.Select(trial.Level.FindEntity).OfType<Entity>()
                .Where(entity => (entity is not Living living || living.Alive)
                    && (entity.Position - center).MagnitudeSquared() <= rangeSquared).ToList()
            : players;
        if (targets.Count == 0)
            return;

        var target = targets.Count == 1 ? targets[0] : targets[trial.Random.NextInt(targets.Count)];
        var above = target.Position + new VectorD(0, target.Dimension.Height + 2f + trial.Random.NextInt(4), 0);
        var terrain = new MobTerrain(trial.Level);
        var hit = terrain.RaycastVisual(target.Position, above, out var unloaded);
        if (unloaded)
            return;

        var cell = hit?.Position ?? (Vector)above.Floor();
        var position = (VectorD)cell + new VectorD(0.5, -0.5, 0.5);
        var below = (Vector)position.Floor();
        var block = terrain.GetBlock(below);
        if (block is null || BlockCollisionShapes.Get(trial.Level, below, block).Count != 0)
            return;

        var spawner = OminousItemSpawner.Create(trial.Level, new ItemStack(item, item.Count));
        spawner.Position = position;
        trial.Level.TrySpawnEntity(spawner, hostOwned: true);
        var pitch = (trial.Random.NextFloat() - trial.Random.NextFloat()) * 0.2f + 1f;
        trial.Level.BlockUpdates.PlaySound((Vector)position.Floor(),
            "minecraft:" + SoundId.Block.TrialSpawnerSpawnItemBegin, pitch: pitch);
        trial.Data = trial.Data with { CooldownEndsAt = trial.Time + TrialSpawnerConfig.TicksBetweenItemSpawners };
    }

    private WeightedEntries<ItemStack> OminousItems(Trial trial)
    {
        if (this.ominousItems is not null)
            return this.ominousItems;

        var resolver = PluginLootTables.CaptureResolver();
        var table = resolver(trial.Config.ItemsToDropWhenOminous) ?? LootTable.Empty;
        var coarse = new Vector(
            (int)MathF.Floor(this.BlockPosition.X / 30f),
            (int)MathF.Floor(this.BlockPosition.Y / 20f),
            (int)MathF.Floor(this.BlockPosition.Z / 30f));
        var seed = unchecked(WorldSeed.Parse(trial.Level.Seed) + PackedPosition(coarse));
        IRandomSource random = seed != 0 ? new LegacyRandomSource(seed) : trial.Level.GrowthRandom;
        if (seed == 0 && table.RandomSequence is string sequence)
            random = BlockLoot.SequencesFor(trial.Level).Get(sequence);

        List<ItemStack> drops;
        lock (random)
            drops = table.GetRandomItems(new LootContext { Random = random, ResolveTable = resolver });
        if (drops.Count == 0)
            return WeightedEntries<ItemStack>.Empty;

        this.ominousItems = new WeightedEntries<ItemStack>(
            [.. drops.Select(drop => (new ItemStack(drop, 1), drop.Count))]);
        return this.ominousItems;
    }
}
