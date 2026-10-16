using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    private static readonly Dictionary<string, float> blastResistance = LoadBlastResistance();

    private static Dictionary<string, float> LoadBlastResistance()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.blast_resistance.json")
            ?? throw new InvalidDataException("Missing block blast resistance data.");
        return JsonSerializer.Deserialize<Dictionary<string, float>>(stream)!;
    }

    internal async ValueTask ExplodeAsync(Entity source, float radius, IEntity? owner = null)
    {
        var random = source is Mob mob ? mob.Random : Random.Shared;
        var terrain = new MobTerrain(this);
        var destroyed = new HashSet<Vector>();
        for (var x = 0; x < 16; x++)
        for (var y = 0; y < 16; y++)
        for (var z = 0; z < 16; z++)
        {
            if (x is not 0 and not 15 && y is not 0 and not 15 && z is not 0 and not 15)
                continue;
            var direction = new VectorD(x / 15f * 2 - 1, y / 15f * 2 - 1, z / 15f * 2 - 1);
            direction /= direction.Magnitude;
            var point = source.Position;
            var strength = radius * (0.7f + random.NextSingle() * 0.6f);
            while (strength > 0)
            {
                var position = (Vector)point.Floor();
                var block = terrain.GetBlock(position);
                if (block == null)
                    break;
                if (!block.IsAir)
                {
                    var resistance = blastResistance[block.UnlocalizedName];
                    if (source is WitherSkull { Invulnerable: true } && !TagsRegistry.Block.WitherImmune.Entries.Contains(block.RegistryId))
                        resistance = Math.Min(resistance, 0.8f);
                    strength -= (resistance + 0.3f) * 0.3f;
                }
                if (strength > 0 && !block.IsAir)
                    destroyed.Add(position);
                point += direction * 0.3f;
                strength -= 0.22500001f;
            }
        }
        var knockbacks = new Dictionary<int, VectorD>();
        var diameter = radius * 2;
        foreach (var target in GetEntitiesInRange(source.Position, diameter + 1).OfType<Entity>().Where(entity => entity is Living or EndCrystal).ToArray())
        {
            if (ReferenceEquals(target, source) || target.Health <= 0 ||
                target is IPlayer player && player.GameMode == GameMode.Spectator)
                continue;
            var distance = (target.Position - source.Position).Magnitude / diameter;
            if (distance > 1)
                continue;
            var direction = target.Position + new VectorD(0, target.Dimension.Height * 0.85f, 0) - source.Position;
            if (direction.Magnitude < 0.000001f)
                continue;
            direction /= direction.Magnitude;
            var bounds = target.Dimension.CreateBBFromPosition(target.Position);
            var total = 0;
            var visible = 0;
            var steps = new VectorD((float)(1 / (bounds.Width * 2 + 1)), (float)(1 / (bounds.Height * 2 + 1)), (float)(1 / (bounds.Depth * 2 + 1)));
            for (var a = 0d; a <= 1; a += steps.X)
            for (var b = 0d; b <= 1; b += steps.Y)
            for (var c = 0d; c <= 1; c += steps.Z)
            {
                total++;
                var sample = bounds.Min + (bounds.Max - bounds.Min) * new VectorD(a, b, c);
                if (terrain.HasLineOfSight(sample, source.Position))
                    visible++;
            }
            var exposure = (1 - distance) * visible / Math.Max(1, total);
            var damage = (float)Math.Floor((exposure * exposure + exposure) / 2 * 7 * diameter + 1);
            if (target is Living living)
                await living.DamageCombatAsync(owner ?? source, damage, CombatDamageKind.Explosion);
            else
                await target.DamageAsync(owner ?? source, damage);
            var knockback = direction * exposure * (1 - Math.Clamp(target.GetAttributeValue("minecraft:generic.knockback_resistance"), 0, 1));
            target.Motion += knockback;
            if (target is IPlayer)
                knockbacks[target.EntityId] = knockback;
        }
        if ((source is Mob || owner is Mob) && !LevelData.GetBooleanRule("mob_griefing")) destroyed.Clear();

        // Drops decay by the explosion radius when the gamerule for the explosion's cause says so, like vanilla's block drops.
        var decays = LevelData.GetBooleanRule(source is Mob || owner is Mob ? "mob_explosion_drop_decay" : "block_explosion_drop_decay");
        var drops = new List<(Vector Position, ItemStack Stack)>();

        // Vanilla visits the destroyed blocks in random order, which decides which stack a drop merges into.
        foreach (var position in destroyed.OrderBy(_ => random.Next()))
        {
            var block = terrain.GetBlock(position)!;
            foreach (var stack in await BlockDrops.ForExplosionAsync(this, block, (VectorD)position, radius, decays))
                AddMergedDrop(drops, position, stack);
            await SetBlockAsync(position, BlocksRegistry.Air, true);
        }

        foreach (var (position, stack) in drops)
            BlockDrops.SpawnDrop(this, () => Server.GetNextEntityId(), position, stack);

        var sound = new SoundEffect { SoundId = "minecraft:entity.generic.explode", SoundCategory = SoundCategory.Blocks,
            Volume = 4, Pitch = 1, Seed = random.NextInt64() };
        foreach (var player in GetPlayersInRange(source.Position, 64))
        {
            var knockback = knockbacks.GetValueOrDefault(player.EntityId);
            await player.QueuePacketAsync(new ExplodePacket
            {
                Center = source.Position, Radius = radius, BlockCount = destroyed.Count,
                PlayerKnockback = new Velocity(knockback.X, knockback.Y, knockback.Z),
                ExplosionParticle = new ExplosionParticleData(), ExplosionSound = sound, ExplosionParticleInfo = []
            });
        }
    }

    /// <summary>
    /// Adds a drop to the first stack of the same item it still fits in, like vanilla's explosion drops.
    /// </summary>
    private static void AddMergedDrop(List<(Vector Position, ItemStack Stack)> drops, Vector position, ItemStack stack)
    {
        for (var index = 0; index < drops.Count; index++)
        {
            var existing = drops[index].Stack;
            if (existing.Equals(stack) && existing.Count + stack.Count <= existing.MaxStackSize)
            {
                existing.Count += stack.Count;
                return;
            }
        }

        drops.Add((position, stack));
    }

    private sealed class ExplosionParticleData : ParticleData
    {
        public override ParticleType ParticleType => ParticleType.ExplosionEmitter;
    }
}
