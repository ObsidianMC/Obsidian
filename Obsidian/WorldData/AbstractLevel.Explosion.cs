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
                    strength -= (blastResistance[block.UnlocalizedName] + 0.3f) * 0.3f;
                if (strength > 0 && !block.IsAir)
                    destroyed.Add(position);
                point += direction * 0.3f;
                strength -= 0.22500001f;
            }
        }
        var knockbacks = new Dictionary<int, VectorD>();
        var diameter = radius * 2;
        foreach (var target in GetEntitiesInRange(source.Position, diameter + 1).OfType<Living>().ToArray())
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
            await target.DamageAsync(owner ?? source, (float)Math.Floor((exposure * exposure + exposure) / 2 * 7 * diameter + 1));
            var knockback = direction * exposure;
            target.Motion += knockback;
            if (target is IPlayer)
                knockbacks[target.EntityId] = knockback;
        }
        foreach (var position in destroyed)
        {
            var block = terrain.GetBlock(position)!;
            await SetBlockAsync(position, BlocksRegistry.Air, true);
            if (random.NextSingle() <= 1 / radius)
            {
                if (ItemsRegistry.TryGet(block.Material, out var holder))
                {
                    var item = new Obsidian.API.Inventory.ItemStack(holder, 1);
                    SpawnEntity(new ItemEntity { Level = this, EntityId = Server.GetNextEntityId(), Position = (VectorD)position + 0.5f, Item = item });
                }
            }
        }
        var sound = new SoundEffect { SoundId = "minecraft:entity.generic.explode", SoundCategory = SoundCategory.Blocks,
            Volume = 4, Pitch = 1, Seed = random.NextInt64() };
        foreach (var player in GetPlayersInRange(source.Position, 64))
        {
            var knockback = knockbacks.GetValueOrDefault(player.EntityId);
            await player.Client.QueuePacketAsync(new ExplodePacket
            {
                Center = source.Position, Radius = radius, BlockCount = destroyed.Count,
                PlayerKnockback = new Velocity(knockback.X, knockback.Y, knockback.Z),
                ExplosionParticle = new ExplosionParticleData(), ExplosionSound = sound, ExplosionParticleInfo = []
            });
        }
    }

    private sealed class ExplosionParticleData : ParticleData
    {
        public override ParticleType ParticleType => ParticleType.ExplosionEmitter;
    }
}
