using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.API.Utilities;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Commands;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
using Obsidian.WorldData;
using Xunit;

namespace Obsidian.Tests;

public sealed partial class VanillaTrialSpawners
{
    [Fact]
    public async Task ReplayOminousTrialReference()
    {
        var reference = LoadFixture("ominous_trials");
        Assert.Equal(ServerConstants.ProtocolDescription, reference.GetProperty("version").GetString());
        var cases = reference.GetProperty("cases").EnumerateArray()
            .Select((probe, index) => (Probe: probe, Origin: new Vector(index * Spacing + 8, 64, 8)))
            .ToArray();
        await using var replay = await TrialWorld.CreateAsync(cases.Length);
        var tables = reference.GetProperty("lootTables").EnumerateObject()
            .Select(table => LootTableJson.Parse(table.Value.GetRawText(), table.Name));
        PluginLootTables.Apply(tables);
        try
        {
            await FixtureCases.AssertAllAsync(cases, item => Name(item.Probe), async item =>
            {
                try
                {
                    await Replay(replay, item.Probe, item.Origin);
                }
                catch (Exception error) when (error is not Xunit.Sdk.XunitException)
                {
                    throw new Xunit.Sdk.XunitException(error.ToString());
                }
            });
        }
        finally
        {
            PluginLootTables.Apply([]);
        }

        FixtureCases.AssertAll(reference.GetProperty("releaseMotion").EnumerateArray(),
            sample => $"motion seed {sample.GetProperty("seed").GetInt64()}", sample =>
            {
                var random = new LegacyRandomSource(sample.GetProperty("seed").GetInt64());
                var motion = OminousItemSpawner.ReleaseMotion(random,
                    sample.GetProperty("power").GetSingle(), sample.GetProperty("spread").GetSingle());
                Assert.Equal(BlockProbes.Point(sample.GetProperty("motion")), motion);
                Assert.Equal(sample.GetProperty("nextInt").GetInt32(), random.NextInt());
            });
        output.WriteLine($"Replayed {cases.Length} ominous trial cases and 4 release-motion probes.");
    }

    // Obsidian-only persistence risk: the countdown's age is not saved by vanilla. Loading restarts its delay,
    // preserves the complete stack, and exposes that stack in the slot metadata clients render.
    [Fact]
    public async Task OminousItemSaveRestartsDelayAndPreservesClientItem()
    {
        await using var replay = await TrialWorld.CreateAsync(1);
        var tag = SnbtReader.ReadCompound("{id:'minecraft:ominous_item_spawner',spawn_item_after_ticks:80L,"
            + "item:{id:'minecraft:lingering_potion',count:1,components:"
            + "{'minecraft:potion_contents':{potion:'minecraft:weaving'}}}}");
        var entity = Assert.IsType<OminousItemSpawner>(EntityNbt.Load(tag, replay.Level));
        for (var tick = 0; tick < 17; tick++)
            await entity.TickAsync();

        var saved = EntityNbt.Save(entity)!;
        var restored = Assert.IsType<OminousItemSpawner>(EntityNbt.Load(saved, replay.Level));
        Assert.Equal(80, saved.GetLong("spawn_item_after_ticks"));
        Assert.Equal(Snbt.Write(tag["item"]), Snbt.Write(restored.Item!.ToNbt()));

        using var metadata = new Obsidian.Net.NetworkBuffer();
        restored.Write(metadata);
        // The base entity's eight fields precede the item slot at index 8. Re-encode the slot to check that the
        // serialized metadata ends in exactly the restored item, including its potion component.
        using var slot = new Obsidian.Net.NetworkBuffer();
        slot.WriteEntityMetadataType(8, EntityMetadataType.Slot);
        slot.WriteItemStack(restored.Item);
        Assert.True(metadata.GetBuffer().AsSpan(0, metadata.Offset)
            .EndsWith(slot.GetBuffer().AsSpan(0, slot.Offset)));

        restored.Position = new VectorD(8.5, 68.5, 8.5);
        Assert.True(replay.Level.TrySpawnEntity(restored, hostOwned: true));
        for (var tick = 0; tick < 79; tick++)
            await restored.TickAsync();

        Assert.Same(restored, replay.Level.FindEntity(restored.Uuid));
        await restored.TickAsync();
        Assert.Null(replay.Level.FindEntity(restored.Uuid));
    }

    private static async Task DiscardReleasedAsync(AbstractLevel level, Vector origin)
    {
        var entities = level.GetNonPlayerEntitiesInRange((VectorD)origin, 32).OfType<Entity>()
            .Where(entity => entity is not Mob and not OminousItemSpawner).ToArray();
        foreach (var entity in entities)
            await entity.RemoveAsync();
    }

    private static void CompareOminous(List<string> differences, int tick, JsonElement expected,
        AbstractLevel level, Vector origin, List<Entities.Player> players, MobLog mobs)
    {
        var effects = new List<string>();
        foreach (var player in players)
        {
            foreach (var effect in new[] { PotionEffect.BadOmen, PotionEffect.TrialOmen })
            {
                if (!player.ActivePotionEffects.TryGetValue((int)effect - 1, out var active))
                    continue;

                var data = active.EffectData;
                var id = effect == PotionEffect.BadOmen ? "bad_omen" : "trial_omen";
                effects.Add($"{player.Username} minecraft:{id} {active.CurrentDuration} {data.Amplifier} "
                    + data.ShowParticles.ToString().ToLowerInvariant() + " "
                    + data.ShowIcon.ToString().ToLowerInvariant());
            }
        }

        var dataRows = new List<string>();
        var min = (VectorD)(origin + new Vector(-Reach, -Below, -Reach));
        var max = (VectorD)(origin + new Vector(Reach + 1, Above + 1, Reach + 1));
        var entities = level.GetNonPlayerEntitiesInRange((VectorD)origin, 32).OfType<Entity>()
            .Where(entity => Overlaps(entity, min, max)).OrderBy(entity => entity.EntityId);
        foreach (var entity in entities)
        {
            if (entity is Mob mob)
            {
                foreach (var (slot, name) in Mob.SavedEquipmentSlots)
                {
                    var item = mob.GetEquipment(slot);
                    if (item.IsAir || item.Count == 0)
                        continue;

                    var chance = mob.GetEquipmentDropChance(slot).ToString("0.0######", CultureInfo.InvariantCulture);
                    dataRows.Add($"{mobs.NameOf(mob.Uuid)} {name} {chance} {Snbt.Write(item.ToNbt())}");
                }
            }
            else if (entity is OminousItemSpawner)
            {
                var saved = EntityNbt.Save(entity)!;
                var own = new NbtCompound();
                if (saved.TryGetTag<NbtCompound>("item", out var item))
                    own.Add(item);

                own.Add(new NbtTag<long>("spawn_item_after_ticks", saved.GetLong("spawn_item_after_ticks")));
                dataRows.Add("item spawner " + Snbt.Write(own));
            }
            else if (entity is ItemEntity)
            {
                var saved = EntityNbt.Save(entity)!;
                var own = new NbtCompound
                {
                    saved["Item"],
                    new NbtTag<short>("PickupDelay", saved.GetShort("PickupDelay"))
                };
                dataRows.Add("released item " + Snbt.Write(own));
            }
            else if (entity is MobProjectile { SplashItem: ItemStack item }
                && entity.Type is EntityType.SplashPotion or EntityType.LingeringPotion)
            {
                dataRows.Add($"released projectile {EntityNbt.TypeId(entity)} {Snbt.Write(item.ToNbt())}");
                using var metadata = new Obsidian.Net.NetworkBuffer();
                entity.Write(metadata);
                using var slot = new Obsidian.Net.NetworkBuffer();
                slot.WriteEntityMetadataType(8, EntityMetadataType.Slot);
                slot.WriteItemStack(item);
                Assert.True(metadata.GetBuffer().AsSpan(0, metadata.Offset)
                    .EndsWith(slot.GetBuffer().AsSpan(0, slot.Offset)));
            }
        }

        Compare(differences, $"tick {tick} effects", Strings(expected.GetProperty("effects")), effects);
        var expectedRows = Strings(expected.GetProperty("data")).Select(CanonicalRow).ToList();
        Compare(differences, $"tick {tick} equipment/item data", expectedRows, dataRows);
    }

    private static string CanonicalRow(string row)
    {
        var start = row.IndexOf('{');
        return start < 0 ? row : row[..start] + Canonical(row[start..]);
    }
}
