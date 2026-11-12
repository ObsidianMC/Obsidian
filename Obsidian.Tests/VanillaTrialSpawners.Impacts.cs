using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Commands;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
using Xunit;

namespace Obsidian.Tests;

public sealed partial class VanillaTrialSpawners
{
    [Fact]
    public async Task ReplayOminousReleaseImpactReference()
    {
        var reference = LoadFixture("ominous_trials");
        await using var replay = await TrialWorld.CreateAsync(1);
        await FixtureCases.AssertAllAsync(reference.GetProperty("impacts").EnumerateArray(), Name,
            probe => ReplayImpact(replay, probe));
        output.WriteLine("Replayed 8 actual ominous releases through impact, including 2 reloads.");
    }

    /// <summary>Loaded chunks alone must not advance this entity's simulation, including its release timer.</summary>
    [Fact]
    public async Task OminousItemCountdownPausesOutsideSimulation()
    {
        await using var replay = await TrialWorld.CreateAsync(1);
        var level = replay.Level;
        var spawner = Assert.IsType<OminousItemSpawner>(EntityNbt.Load(SnbtReader.ReadCompound(
            "{id:'minecraft:ominous_item_spawner',spawn_item_after_ticks:2L,item:{id:'minecraft:stick',count:1}}"), level));
        spawner.Position = new VectorD(8.5, 68.5, 8.5);
        Assert.True(level.TrySpawnEntity(spawner, hostOwned: true));
        Assert.NotNull(level.GetLoadedChunk(0, 0));
        Assert.False(level.IsMobTicking(spawner.Position));

        for (var tick = 0; tick < 4; tick++)
            await spawner.TickAsync();

        Assert.Same(spawner, level.FindEntity(spawner.Uuid));
        using (level.AddChunkTicket(0, 0))
            await spawner.TickAsync();

        for (var tick = 0; tick < 4; tick++)
            await spawner.TickAsync();

        Assert.Same(spawner, level.FindEntity(spawner.Uuid));
        using (level.AddChunkTicket(0, 0))
            await spawner.TickAsync();

        Assert.Null(level.FindEntity(spawner.Uuid));
        Assert.Single(level.GetNonPlayerEntitiesInRange(spawner.Position, 4).OfType<ItemEntity>());
    }

    private static async Task ReplayImpact(TrialWorld replay, JsonElement probe)
    {
        var origin = new Vector(8, 64, 8);
        var level = replay.Level;
        using var ticket = level.AddChunkTicket(0, 0);
        var blocks = new JsonArray(new JsonObject
        {
            ["pos"] = JsonSerializer.SerializeToNode(new[] { -8, -1, -8 }),
            ["to"] = JsonSerializer.SerializeToNode(new[] { 8, -1, 8 }),
            ["state"] = "minecraft:stone"
        });
        Setup(level, JsonSerializer.SerializeToElement(new { setup = blocks }), origin);
        var specs = new JsonArray();
        foreach (var target in probe.GetProperty("targets").EnumerateArray())
        {
            var spec = JsonNode.Parse(target.GetRawText())!;
            spec["uuid"] = Guid.NewGuid().ToString();
            specs.Add(spec);
        }
        var players = replay.AddPlayers(JsonSerializer.SerializeToElement(new { players = specs }), origin);
        var listener = replay.Listen(origin + ListenerOffset, Name(probe));
        level.GrowthRandom = new LegacyRandomSource(probe.GetProperty("seed").GetInt64());
        level.SoundSeedRandom = new LegacyRandomSource(probe.GetProperty("seed").GetInt64());
        var tag = SnbtReader.ReadCompound("{id:'minecraft:ominous_item_spawner',spawn_item_after_ticks:1L,item:"
            + probe.GetProperty("stack").GetString() + "}");
        var spawner = Assert.IsType<OminousItemSpawner>(EntityNbt.Load(tag, level));
        spawner.Position = (VectorD)origin + BlockProbes.Point(probe.GetProperty("pos"));
        spawner.ShotRandom = new LegacyRandomSource(probe.GetProperty("shotSeed").GetInt64());
        Assert.True(level.TrySpawnEntity(spawner, hostOwned: true));
        listener.Client.ClearPackets();

        // The real release chooses the projectile type, components, owner, speed and spread.
        await spawner.TickAsync();
        var projectile = Assert.Single(level.GetNonPlayerEntitiesInRange((VectorD)origin, 16).OfType<Entity>());
        var expected = probe.GetProperty("output");
        var differences = new List<string>();
        BlockProbes.Expect(differences, "released type", expected.GetProperty("type").GetString(),
            EntityNbt.TypeId(projectile));
        ExpectVector(differences, "birth motion", BlockProbes.Point(expected.GetProperty("birthMotion")),
            projectile.Motion);
        BlockProbes.Expect(differences, "saved released item", Canonical(expected.GetProperty("item").GetString()!),
            Snbt.Write(ProjectileItem(EntityNbt.Save(projectile)!)));

        // Save and restore the actual released projectile before it ever impacts. Neither branch rebuilds the item
        // from fixture data: a lost component must survive into its appearance and its eventual hit effect.
        if (probe.GetProperty("reload").GetBoolean())
        {
            var saved = EntityNbt.Save(projectile)!;
            await projectile.RemoveAsync();
            projectile = EntityNbt.Load(saved, level)!;
            Assert.True(level.TrySpawnEntity(projectile, hostOwned: true));
            var item = ProjectileItem(EntityNbt.Save(projectile)!);
            BlockProbes.Expect(differences, "reloaded item",
                Canonical(expected.GetProperty("reloadedItem").GetString()!), Snbt.Write(item));
            if (projectile is MobProjectile potion)
            {
                using var metadata = new Obsidian.Net.NetworkBuffer();
                potion.Write(metadata);
                using var slot = new Obsidian.Net.NetworkBuffer();
                slot.WriteEntityMetadataType(8, EntityMetadataType.Slot);
                slot.WriteItemStack(SnbtReader.ReadCompound(probe.GetProperty("stack").GetString()!).ItemFromNbt());
                if (!metadata.GetBuffer().AsSpan(0, metadata.Offset)
                    .EndsWith(slot.GetBuffer().AsSpan(0, slot.Offset)))
                {
                    differences.Add("reloaded potion metadata lost its full item stack");
                }
            }
        }

        var ticks = 0;
        while (level.FindEntity(projectile.Uuid) is not null && ticks < 8)
        {
            await projectile.TickAsync();
            ticks++;
        }
        BlockProbes.Expect(differences, "impact ticks", expected.GetProperty("impactTicks").GetInt32(), ticks);
        BlockProbes.Expect(differences, "removed", expected.GetProperty("removed").GetBoolean(),
            level.FindEntity(projectile.Uuid) is null);

        for (var index = 0; index < players.Count; index++)
        {
            var player = players[index];
            var victim = expected.GetProperty("targets")[index];
            BlockProbes.Expect(differences, $"target {index} health", victim.GetProperty("health").GetSingle(),
                player.Health);
            BlockProbes.Expect(differences, $"target {index} burning", victim.GetProperty("burning").GetBoolean(),
                player.Burning);
            var effects = player.ActivePotionEffects.OrderBy(effect => effect.Key).Select(effect =>
                "minecraft:" + ((PotionEffect)(effect.Key + 1)).ToString().ToSnakeCase() + " "
                + effect.Value.CurrentDuration + " " + effect.Value.EffectData.Amplifier).ToList();
            Compare(differences, $"target {index} effects", Strings(victim.GetProperty("effects")), effects);
            if (projectile.Type == EntityType.WindCharge)
            {
                ExpectVector(differences, $"target {index} knockback", BlockProbes.Point(victim.GetProperty("motion")),
                    player.Motion);
            }
        }

        var events = listener.Client.SentPackets.OfType<LevelEventPacket>()
            .Select(packet => $"{packet.Type} {packet.Data}").ToList();
        Compare(differences, "release and impact events", Strings(expected.GetProperty("levelEvents")), events);
        var explosions = listener.Client.SentPackets.OfType<ExplodePacket>().ToArray();
        var native = expected.GetProperty("explosions");
        BlockProbes.Expect(differences, "wind explosion packets", native.GetArrayLength(), explosions.Length);
        for (var index = 0; index < native.GetArrayLength() && index < explosions.Length; index++)
        {
            var packet = explosions[index];
            var sample = native[index];
            ExpectVector(differences, "wind burst center", BlockProbes.Point(sample.GetProperty("center")),
                packet.Center - (VectorD)origin);
            BlockProbes.Expect(differences, "wind burst power", sample.GetProperty("radius").GetSingle(), packet.Radius);
            BlockProbes.Expect(differences, "wind reached blocks", sample.GetProperty("blocks").GetInt32(),
                packet.BlockCount);
            BlockProbes.Expect(differences, "wind burst particle", sample.GetProperty("particle").GetString(),
                "minecraft:" + packet.ExplosionParticle.ParticleType.ToString().ToSnakeCase());
            BlockProbes.Expect(differences, "wind burst sound", sample.GetProperty("sound").GetString(),
                packet.ExplosionSound.SoundId);
        }
        BlockProbes.Expect(differences, "fire blocks", expected.GetProperty("fireBlocks").GetInt64(),
            Area(origin).LongCount(position => level.BlockUpdates.GetBlock(position).Is(BlockIds.Fire)));

        replay.RemovePlayers(players);
        await replay.ClearAsync(origin);
        Assert.True(differences.Count == 0, string.Join("\n", differences));
    }

    private static NbtCompound ProjectileItem(NbtCompound tag)
    {
        if (tag.TryGetTag<NbtCompound>("Item", out var upper))
            return upper;

        return tag.TryGetTag<NbtCompound>("item", out var lower) ? lower : new NbtCompound();
    }

    private static void ExpectVector(List<string> differences, string label, VectorD expected, VectorD actual)
    {
        var error = (expected - actual).Magnitude;
        if (!double.IsFinite(error) || error > 1e-7)
            differences.Add($"{label}: vanilla {expected}, Obsidian {actual}");
    }
}
