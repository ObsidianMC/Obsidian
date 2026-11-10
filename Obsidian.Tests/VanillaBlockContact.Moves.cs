using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Loot;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace Obsidian.Tests;

public partial class VanillaBlockContact
{
    private sealed partial class Replay
    {
        private async Task<List<string>> RunContactMovesAsync(JsonElement scenario)
        {
            var world = this.context.World;
            var min = ReadVector(scenario.GetProperty("min"));
            var max = ReadVector(scenario.GetProperty("max"));
            const BlockUpdateFlags quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;
            for (var y = min.Y; y <= max.Y; y++)
            {
                for (var z = min.Z; z <= max.Z; z++)
                {
                    for (var x = min.X; x <= max.X; x++)
                        world.BlockUpdates.SetBlock(new Vector(x, y, z), BlocksRegistry.Air, quiet);
                }
            }

            foreach (var placement in scenario.GetProperty("setup").EnumerateArray())
            {
                world.BlockUpdates.SetBlock(ReadVector(placement.GetProperty("pos")),
                    ParseState(placement.GetProperty("state").GetString()!), quiet);
            }

            var mobGriefing = !scenario.TryGetProperty("mobGriefing", out var griefing) || griefing.GetBoolean();
            world.LevelData.GameRules["mob_griefing"] = mobGriefing.ToString();
            world.GrowthRandom = new LegacyRandomSource(scenario.GetProperty("seed").GetInt64());
            world.TakeGameEvents();
            var entities = new Dictionary<string, Entity>();
            var terrain = new MobTerrain(world);
            var contacts = new EntityContactPass((IContactLevel)world);
            var differences = new List<string>();
            var writes = new List<string>();
            var observer = (TestClient)this.contactObserver.Client;
            observer.ClearPackets();
            world.Fluids.BlockStored = (pos, block, flags) => writes.Add(Write(pos, block, flags));
            try
            {
                foreach (var checkpoint in scenario.GetProperty("checkpoints").EnumerateArray())
                {
                    var tick = checkpoint.GetProperty("tick").GetInt32();
                    writes.Clear();
                    observer.ClearPackets();
                    world.TakeGameEvents();
                    foreach (var entry in scenario.GetProperty("entities").EnumerateArray())
                    {
                        if (entry.GetProperty("tick").GetInt32() != tick)
                            continue;

                        if (entry.TryGetProperty("removeJumpBoost", out var effectTarget))
                        {
                            ((Living)entities[effectTarget.GetString()!])
                                .RemovePotionEffect((int)PotionEffect.JumpBoost - 1);
                            continue;
                        }

                        if (entry.TryGetProperty("velocity", out var tag))
                        {
                            entities[tag.GetString()!].Motion = ReadLocation(entry.GetProperty("motion"));
                            continue;
                        }

                        var entity = Summon(world, entry);
                        PrepareContactEntity(entity, entry);
                        entities[entry.GetProperty("summon").GetString()!] = entity;
                    }

                    if (tick > 0)
                    {
                        foreach (var entity in entities.Values)
                        {
                            entity.Position = EntityMovement.Displace(entity, terrain, entity.Motion);
                            await contacts.TouchAsync(entity);
                        }
                    }

                    await world.BlockNotifications.DrainAsync();
                    CompareContactCheckpoint(checkpoint, entities, writes, observer, differences);
                }
            }
            finally
            {
                world.Fluids.BlockStored = null;
                foreach (var entity in entities.Values)
                    await world.DestroyEntityAsync(entity);
            }

            var nextRandom = world.GrowthRandom.NextInt();
            var expectedRandom = scenario.GetProperty("nextRandom").GetInt32();
            if (nextRandom != expectedRandom)
                differences.Add($"random consumption: expected {expectedRandom}, got {nextRandom}");

            return differences;
        }

        private static void PrepareContactEntity(Entity entity, JsonElement entry)
        {
            if (entity is Living living && entry.TryGetProperty("health", out var health))
                living.Health = health.GetSingle();

            entity.Sneaking = entry.TryGetProperty("sneaking", out var sneak) && sneak.GetBoolean();
            if (entry.TryGetProperty("fallDistance", out var fall))
                entity.ContactFallDistance = fall.GetDouble();

            if (entity is ItemEntity item)
                item.CanPickup = false;

            if (entity is Mob mob)
            {
                // Match the capture's seeded entity random without changing the production random implementation.
                var random = new ContactRandom(entry.GetProperty("entitySeed").GetInt64());
                typeof(Mob).GetProperty("Random", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(mob, random);
                if (entry.TryGetProperty("baby", out _) && mob is Animal animal)
                    animal.Age = -24000;

                if (entry.TryGetProperty("jumpBoost", out var jumpBoost))
                    mob.AddPotionEffect((int)PotionEffect.JumpBoost - 1, 1000, jumpBoost.GetInt32());

                if (entry.TryGetProperty("fireResistance", out _))
                    mob.AddPotionEffect((int)PotionEffect.FireResistance - 1, 1000);

                if (entry.TryGetProperty("frostWalker", out _))
                {
                    var boots = new ItemStack(ItemsRegistry.DiamondBoots, 1);
                    EnchantmentHelper.Enchant(boots, EnchantmentsRegistry.FrostWalker, 1);
                    var slot = entry.TryGetProperty("frostWalkerSlot", out _)
                        ? EquipmentSlot.Helmet : EquipmentSlot.Boots;
                    mob.SetEquipment(slot, boots);
                }
            }
        }

        private void CompareContactCheckpoint(
            JsonElement checkpoint,
            Dictionary<string, Entity> entities,
            List<string> writes,
            TestClient observer,
            List<string> differences)
        {
            var world = this.context.World;
            var tick = checkpoint.GetProperty("tick").GetInt32();
            foreach (var placement in checkpoint.GetProperty("blocks").EnumerateArray())
            {
                var pos = ReadVector(placement.GetProperty("pos"));
                var expected = placement.GetProperty("state").GetString()!;
                var actual = world.BlockUpdates.GetBlock(pos);
                if (!actual.IsSameState(ParseState(expected)))
                    differences.Add($"tick {tick} block {pos}: expected {expected}, got {Describe(actual, expected)}");
            }

            foreach (var observation in checkpoint.GetProperty("observations").EnumerateObject())
            {
                var entity = entities[observation.Name];
                var expected = observation.Value;
                CompareVector("position", expected.GetProperty("position"), entity.Position);
                CompareVector("velocity", expected.GetProperty("velocity"), entity.Motion);
                CompareNumber("fall distance", expected.GetProperty("fallDistance").GetDouble(),
                    entity.ContactFallDistance);
                var grounded = entity.MovementFlags.HasFlag(MovementFlags.OnGround);
                if (expected.GetProperty("onGround").GetBoolean() != grounded)
                    differences.Add($"tick {tick} {observation.Name} grounded: got {grounded}");

                if (entity is Living living)
                {
                    CompareNumber("health", expected.GetProperty("health").GetDouble(), living.Health);
                    if (expected.TryGetProperty("safeFallDistance", out var safe))
                    {
                        CompareNumber("safe fall distance", safe.GetDouble(),
                            living.GetAttributeValue("minecraft:safe_fall_distance"));
                    }
                    var damageType = living.LastDamageKind switch
                    {
                        null => null,
                        CombatDamageKind.Fall => "minecraft:fall",
                        CombatDamageKind.HotFloor => "minecraft:hot_floor",
                        _ => $"unexpected:{living.LastDamageKind}"
                    };
                    var expectedType = expected.GetProperty("damageType").GetString();
                    if (damageType != expectedType)
                        differences.Add($"tick {tick} damage type: expected {expectedType}, got {damageType}");
                }

                void CompareVector(string field, JsonElement value, VectorD actual)
                {
                    var reference = ReadLocation(value);
                    if (VectorD.Distance(reference, actual) > PositionTolerance)
                        differences.Add($"tick {tick} {observation.Name} {field}: {reference} != {actual}");
                }

                void CompareNumber(string field, double reference, double actual)
                {
                    if (Math.Abs(reference - actual) > PositionTolerance)
                        differences.Add($"tick {tick} {observation.Name} {field}: {reference} != {actual}");
                }
            }

            CompareList("writes", checkpoint.GetProperty("writes").EnumerateArray().Select(write => Write(
                ReadVector(write.GetProperty("pos")), ParseState(write.GetProperty("state").GetString()!),
                (BlockUpdateFlags)write.GetProperty("flags").GetInt32())), writes);
            var packets = observer.SentPackets.ToArray();
            var sounds = packets.Select(packet => packet switch
            {
                SoundPacket sound => sound.SoundLocation,
                SoundEntityPacket sound => sound.SoundLocation,
                _ => null
            }).OfType<string>();
            CompareList("sounds", Strings(checkpoint.GetProperty("sounds")), sounds);
            var heard = packets.Select(packet => packet switch
            {
                SoundPacket sound => new SoundObservation(sound.SoundLocation, sound.Category,
                    sound.Volume, sound.Pitch,
                    new VectorD(sound.SoundPosition.X / 8.0, sound.SoundPosition.Y / 8.0,
                        sound.SoundPosition.Z / 8.0)),
                SoundEntityPacket sound => new SoundObservation(sound.SoundLocation, sound.Category,
                    sound.Volume, sound.Pitch, entities.Values.Single(entity => entity.EntityId == sound.EntityId)
                        .Position),
                _ => null
            }).OfType<SoundObservation>().ToArray();
            var details = checkpoint.GetProperty("soundDetails").EnumerateArray().ToArray();
            if (details.Length != heard.Length)
                differences.Add($"tick {tick} sound details: expected {details.Length}, got {heard.Length}");

            for (var index = 0; index < Math.Min(details.Length, heard.Length); index++)
            {
                var reference = details[index];
                var actual = heard[index];
                var category = actual.Category switch
                {
                    SoundCategory.Blocks => "block",
                    SoundCategory.Players => "player",
                    SoundCategory.Records => "record",
                    _ => actual.Category.ToString().ToLowerInvariant()
                };
                if (reference.GetProperty("id").GetString() != actual.Id
                    || reference.GetProperty("category").GetString() != category
                    || Math.Abs(reference.GetProperty("volume").GetDouble() - actual.Volume) > PositionTolerance
                    || Math.Abs(reference.GetProperty("pitch").GetDouble() - actual.Pitch) > PositionTolerance
                    || VectorD.Distance(ReadLocation(reference.GetProperty("position")), actual.Position)
                        > PositionTolerance)
                {
                    differences.Add($"tick {tick} sound {index}: expected {reference}, got {actual}");
                }
            }
            var levelEvents = packets.OfType<LevelEventPacket>()
                .Select(evt => $"{evt.Type} {Offset(evt.Position)} {evt.Data}");
            CompareList("level events", Strings(checkpoint.GetProperty("levelEvents")), levelEvents);
            var gameEvents = world.TakeGameEvents().Select(evt =>
                $"{evt.Kind.Identifier()} {Offset((Vector)evt.Position.Floor())} "
                + (evt.Source is null ? "none" : "minecraft:" + evt.Source.Type.ToString().ToSnakeCase()));
            CompareList("game events", Strings(checkpoint.GetProperty("gameEvents")), gameEvents);
            CompareList("block events", Strings(checkpoint.GetProperty("blockEvents")),
                packets.OfType<BlockEventPacket>().Select(evt => evt.ToString()));
            var drops = world.GetNonPlayerEntitiesInRange(new VectorD(0.5, 65, 0.5), 8)
                .OfType<ItemEntity>().Where(item => !entities.ContainsValue(item))
                .Select(item => $"{item.Item.Holder.UnlocalizedName} x{item.Item.Count}");
            CompareList("drops", Strings(checkpoint.GetProperty("drops")), drops);

            void CompareList(string field, IEnumerable<string> expected, IEnumerable<string> actual)
            {
                var reference = expected.ToArray();
                var observed = actual.ToArray();
                if (!reference.SequenceEqual(observed))
                {
                    differences.Add($"tick {tick} {field}: [{string.Join("; ", reference)}] != "
                        + $"[{string.Join("; ", observed)}]");
                }
            }
        }

        private sealed record SoundObservation(
            string Id, SoundCategory Category, float Volume, float Pitch, VectorD Position);

        private sealed class ContactRandom(long seed) : Random
        {
            private readonly LegacyRandomSource source = new(seed);

            public override float NextSingle() => this.source.NextFloat();

            public override long NextInt64() => this.source.NextLong();
        }

        private static IEnumerable<string> Strings(JsonElement array) =>
            array.EnumerateArray().Select(value => value.GetString()!);

        private static string Offset(Vector pos) => $"{pos.X},{pos.Y - 64},{pos.Z}";

        private static string Write(Vector pos, IBlock block, BlockUpdateFlags flags) =>
            $"{Offset(pos)}={Describe(block, FullState(block))} flags {(int)flags}";

        private static string FullState(IBlock block)
        {
            var properties = block.GetProperties().OrderBy(pair => pair.Key)
                .Select(pair => $"{pair.Key}={pair.Value}").ToArray();
            return properties.Length == 0 ? block.UnlocalizedName
                : $"{block.UnlocalizedName}[{string.Join(",", properties)}]";
        }
    }
}
